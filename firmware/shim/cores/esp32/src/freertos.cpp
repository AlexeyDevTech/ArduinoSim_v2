// Cooperative FreeRTOS tasks for the ESP32 core (see freertos/FreeRTOS.h, ADR-009).
// Every task runs on its own OS thread, but only the task in Scheduler::current runs: a task
// hands over by signalling the next one and waiting for its own signal, so host calls stay
// serialized. Time passes only through host calls; when no task is due, the running thread
// asks the host to advance to the earliest wake-up.
#include <Arduino.h>
#include <stdio.h>
#include <string.h>

#include <vector>

#include "freertos/FreeRTOS.h"
#include "sim/host.h"

using sim::host;

namespace {

constexpr uint64_t Forever = UINT64_MAX;
constexpr size_t TaskStackReserve = 1024 * 1024;

enum class TaskState { Ready, Sleeping, Waiting, Suspended, Deleted };

enum class QueueKind { Queue, Binary, Counting, Mutex, RecursiveMutex };

}  // namespace

struct sim_task {
    TaskFunction_t code;
    void* parameters;
    char name[16];
    UBaseType_t priority;
    uint32_t stack_depth;
    BaseType_t core;
    void* signal;
    TaskState state;
    uint64_t wake_us;
    const void* waiting_on;
    bool timed_out;
    uint32_t notify_value;
    bool notify_pending;
};

struct sim_queue {
    QueueKind kind;
    UBaseType_t length;
    UBaseType_t item_size;
    std::vector<uint8_t> storage;
    UBaseType_t head;
    UBaseType_t count;
    sim_task* holder;
    UBaseType_t recursion;
};

namespace {

struct Scheduler {
    std::vector<sim_task*> tasks;
    sim_task* current = nullptr;
    sim_task loop_task{};
    int critical_nesting = 0;
    bool warned_watermark = false;
};

Scheduler& scheduler()
{
    static Scheduler instance;
    return instance;
}

// The thread that first reaches the scheduler (the Runner's simulation thread, which runs
// setup() and loop()) becomes the loopTask, as on the real core.
sim_task* self()
{
    Scheduler& s = scheduler();
    if (s.current == nullptr) {
        sim_task& loop = s.loop_task;
        strcpy(loop.name, "loopTask");
        loop.priority = 1;
        loop.stack_depth = 8192;
        loop.core = ARDUINO_RUNNING_CORE;
        loop.signal = sim::signal_create();
        loop.state = TaskState::Ready;
        s.tasks.push_back(&loop);
        s.current = &loop;
    }
    return s.current;
}

uint64_t now_us() { return host().now_us(); }

bool due(const sim_task* task, uint64_t now)
{
    return task->state == TaskState::Ready ||
           ((task->state == TaskState::Sleeping || task->state == TaskState::Waiting) && task->wake_us <= now);
}

// The highest-priority task that is due; among equals, the first after the current one, so
// tasks of one priority take turns.
sim_task* pick(Scheduler& s, uint64_t now)
{
    size_t count = s.tasks.size();
    size_t start = 0;
    for (size_t i = 0; i < count; i++) {
        if (s.tasks[i] == s.current) {
            start = i + 1;
            break;
        }
    }

    sim_task* best = nullptr;
    for (size_t k = 0; k < count; k++) {
        sim_task* task = s.tasks[(start + k) % count];
        if (due(task, now) && (best == nullptr || task->priority > best->priority)) {
            best = task;
        }
    }
    return best;
}

// The running task has set its own state; returns when it is chosen to run again.
void reschedule()
{
    Scheduler& s = scheduler();
    sim_task* me = s.current;
    for (;;) {
        uint64_t now = now_us();
        sim_task* next = pick(s, now);
        if (next != nullptr) {
            if (next->state == TaskState::Waiting) {
                next->timed_out = true;
            }
            if (next->state != TaskState::Ready) {
                next->state = TaskState::Ready;
            }
            if (next != me) {
                s.current = next;
                sim::signal_set(next->signal);
                sim::signal_wait(me->signal);
            }
            return;
        }

        uint64_t earliest = Forever;
        for (sim_task* task : s.tasks) {
            if ((task->state == TaskState::Sleeping || task->state == TaskState::Waiting) && task->wake_us < earliest) {
                earliest = task->wake_us;
            }
        }

        uint64_t wait = earliest == Forever ? 1000000u : earliest - now;
        host().delay_us(static_cast<uint32_t>(wait > 1000000000u ? 1000000000u : (wait == 0 ? 1 : wait)));
    }
}

void wake_waiters(const void* object)
{
    for (sim_task* task : scheduler().tasks) {
        if (task->state == TaskState::Waiting && task->waiting_on == object) {
            task->timed_out = false;
            task->state = TaskState::Ready;
        }
    }
}

uint64_t deadline_after(TickType_t ticks) { return ticks == portMAX_DELAY ? Forever : now_us() + static_cast<uint64_t>(ticks) * 1000u; }

// Blocks the running task until ready() holds or the ticks run out.
template <class Ready>
bool wait_for(const void* object, TickType_t ticks, Ready ready)
{
    if (ready()) {
        return true;
    }
    if (ticks == 0) {
        return false;
    }

    sim_task* me = self();
    uint64_t deadline = deadline_after(ticks);
    for (;;) {
        me->state = TaskState::Waiting;
        me->waiting_on = object;
        me->wake_us = deadline;
        me->timed_out = false;
        reschedule();
        me->waiting_on = nullptr;
        if (ready()) {
            return true;
        }
        if (me->timed_out || now_us() >= deadline) {
            return false;
        }
    }
}

void task_thread(void* argument)
{
    sim_task* task = static_cast<sim_task*>(argument);
    sim::signal_wait(task->signal);
    task->code(task->parameters);
    char message[96];
    snprintf(message, sizeof message, "FreeRTOS task \"%s\" returned; ESP-IDF would abort, the simulation deletes it.", task->name);
    sim::log_warning(message);
    vTaskDelete(nullptr);
}

sim_task* resolve(TaskHandle_t task) { return task != nullptr ? task : self(); }

sim_queue* new_queue(QueueKind kind, UBaseType_t length, UBaseType_t item_size, UBaseType_t initial)
{
    sim_queue* queue = new sim_queue{};
    queue->kind = kind;
    queue->length = length;
    queue->item_size = item_size;
    queue->storage.resize(static_cast<size_t>(length) * item_size);
    queue->count = initial;
    return queue;
}

BaseType_t queue_send(sim_queue* queue, const void* item, TickType_t ticks, bool front)
{
    if (queue == nullptr || !wait_for(queue, ticks, [queue]() { return queue->count < queue->length; })) {
        return errQUEUE_FULL;
    }

    if (queue->item_size > 0) {
        UBaseType_t slot;
        if (front) {
            queue->head = (queue->head + queue->length - 1) % queue->length;
            slot = queue->head;
        } else {
            slot = (queue->head + queue->count) % queue->length;
        }
        memcpy(&queue->storage[static_cast<size_t>(slot) * queue->item_size], item, queue->item_size);
    }

    queue->count++;
    wake_waiters(queue);
    return pdPASS;
}

BaseType_t queue_receive(sim_queue* queue, void* buffer, TickType_t ticks, bool peek)
{
    if (queue == nullptr || !wait_for(queue, ticks, [queue]() { return queue->count > 0; })) {
        return errQUEUE_EMPTY;
    }

    if (queue->item_size > 0 && buffer != nullptr) {
        memcpy(buffer, &queue->storage[static_cast<size_t>(queue->head) * queue->item_size], queue->item_size);
    }

    if (!peek) {
        if (queue->item_size > 0) {
            queue->head = (queue->head + 1) % queue->length;
        }
        queue->count--;
        wake_waiters(queue);
    }
    return pdPASS;
}

}  // namespace

extern "C" {

BaseType_t xTaskCreatePinnedToCore(TaskFunction_t code, const char* name, uint32_t stack_depth, void* parameters, UBaseType_t priority, TaskHandle_t* created, BaseType_t core_id)
{
    Scheduler& s = scheduler();
    sim_task* creator = self();
    sim_task* task = new sim_task{};
    task->code = code;
    task->parameters = parameters;
    snprintf(task->name, sizeof task->name, "%s", name != nullptr ? name : "");
    task->priority = priority < configMAX_PRIORITIES ? priority : configMAX_PRIORITIES - 1;
    task->stack_depth = stack_depth;
    task->core = core_id;
    task->signal = sim::signal_create();
    task->state = TaskState::Ready;
    if (task->signal == nullptr || !sim::thread_start(task_thread, task, TaskStackReserve)) {
        delete task;
        return pdFAIL;
    }

    s.tasks.push_back(task);
    if (created != nullptr) {
        *created = task;
    }

    // As in FreeRTOS, a new task of higher priority than its creator runs at once.
    if (task->priority > creator->priority) {
        creator->state = TaskState::Ready;
        reschedule();
    }
    return pdPASS;
}

BaseType_t xTaskCreate(TaskFunction_t code, const char* name, uint32_t stack_depth, void* parameters, UBaseType_t priority, TaskHandle_t* created)
{
    return xTaskCreatePinnedToCore(code, name, stack_depth, parameters, priority, created, tskNO_AFFINITY);
}

void vTaskDelete(TaskHandle_t handle)
{
    Scheduler& s = scheduler();
    sim_task* task = resolve(handle);
    task->state = TaskState::Deleted;
    for (size_t i = 0; i < s.tasks.size(); i++) {
        if (s.tasks[i] == task) {
            s.tasks.erase(s.tasks.begin() + static_cast<long long>(i));
            break;
        }
    }

    // A deleted task's thread stays parked on its signal, which nobody sets again.
    if (task == s.current) {
        reschedule();
        for (;;) {
            sim::signal_wait(task->signal);
        }
    }
}

void vTaskDelay(TickType_t ticks)
{
    if (ticks == 0) {
        sim_task_yield();
        return;
    }

    sim_task* me = self();
    me->state = TaskState::Sleeping;
    me->wake_us = now_us() + static_cast<uint64_t>(ticks) * 1000u;
    reschedule();
}

BaseType_t xTaskDelayUntil(TickType_t* previous_wake, TickType_t increment)
{
    TickType_t target = *previous_wake + increment;
    TickType_t now = xTaskGetTickCount();
    *previous_wake = target;
    if (static_cast<int32_t>(target - now) <= 0) {
        sim_task_yield();
        return pdFALSE;
    }

    vTaskDelay(target - now);
    return pdTRUE;
}

TickType_t xTaskGetTickCount(void) { return static_cast<TickType_t>(now_us() / 1000u); }

TickType_t xTaskGetTickCountFromISR(void) { return xTaskGetTickCount(); }

void vTaskSuspend(TaskHandle_t handle)
{
    sim_task* task = resolve(handle);
    task->state = TaskState::Suspended;
    if (task == scheduler().current) {
        reschedule();
    }
}

void vTaskResume(TaskHandle_t handle)
{
    if (handle != nullptr && handle->state == TaskState::Suspended) {
        handle->state = TaskState::Ready;
    }
}

BaseType_t xTaskResumeFromISR(TaskHandle_t handle)
{
    vTaskResume(handle);
    return pdFALSE;
}

TaskHandle_t xTaskGetCurrentTaskHandle(void) { return self(); }

TaskHandle_t xTaskGetHandle(const char* name)
{
    self();
    for (sim_task* task : scheduler().tasks) {
        if (strcmp(task->name, name) == 0) {
            return task;
        }
    }
    return nullptr;
}

char* pcTaskGetName(TaskHandle_t handle) { return resolve(handle)->name; }

UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t handle)
{
    Scheduler& s = scheduler();
    if (!s.warned_watermark) {
        s.warned_watermark = true;
        sim::log_warning("uxTaskGetStackHighWaterMark(): stack use is not measured; it returns half of the task's stack size.");
    }
    return resolve(handle)->stack_depth / 2;
}

UBaseType_t uxTaskPriorityGet(TaskHandle_t handle) { return resolve(handle)->priority; }

void vTaskPrioritySet(TaskHandle_t handle, UBaseType_t priority)
{
    resolve(handle)->priority = priority < configMAX_PRIORITIES ? priority : configMAX_PRIORITIES - 1;
    sim_task_yield();
}

UBaseType_t uxTaskGetNumberOfTasks(void)
{
    self();
    return static_cast<UBaseType_t>(scheduler().tasks.size());
}

eTaskState eTaskGetState(TaskHandle_t handle)
{
    sim_task* task = resolve(handle);
    switch (task->state) {
    case TaskState::Ready:
        return task == scheduler().current ? eRunning : eReady;
    case TaskState::Sleeping:
    case TaskState::Waiting:
        return eBlocked;
    case TaskState::Suspended:
        return eSuspended;
    case TaskState::Deleted:
        return eDeleted;
    }
    return eInvalid;
}

BaseType_t xPortGetCoreID(void) { return ARDUINO_RUNNING_CORE; }

BaseType_t xTaskGetAffinity(TaskHandle_t handle) { return resolve(handle)->core; }

void sim_task_switch_point(void)
{
    Scheduler& s = scheduler();
    if (s.current != nullptr && s.tasks.size() > 1) {
        sim_task_yield();
    }
}

void sim_task_yield(void)
{
    sim_task* me = self();
    host().yield();
    me->state = TaskState::Ready;
    reschedule();
}

// Tasks never preempt each other here, so suspending the scheduler changes nothing.
void vTaskSuspendAll(void) {}

BaseType_t xTaskResumeAll(void) { return pdFALSE; }

BaseType_t xTaskNotify(TaskHandle_t handle, uint32_t value, eNotifyAction action)
{
    sim_task* task = resolve(handle);
    if (action == eSetValueWithoutOverwrite && task->notify_pending) {
        return pdFAIL;
    }

    switch (action) {
    case eSetBits:
        task->notify_value |= value;
        break;
    case eIncrement:
        task->notify_value++;
        break;
    case eSetValueWithOverwrite:
    case eSetValueWithoutOverwrite:
        task->notify_value = value;
        break;
    case eNoAction:
        break;
    }

    task->notify_pending = true;
    wake_waiters(task);
    return pdPASS;
}

uint32_t ulTaskNotifyTake(BaseType_t clear_on_exit, TickType_t ticks)
{
    sim_task* me = self();
    if (!wait_for(me, ticks, [me]() { return me->notify_value > 0; })) {
        return 0;
    }

    uint32_t value = me->notify_value;
    me->notify_value = clear_on_exit ? 0 : value - 1;
    me->notify_pending = me->notify_value > 0;
    return value;
}

BaseType_t xTaskNotifyWait(uint32_t clear_on_entry, uint32_t clear_on_exit, uint32_t* value, TickType_t ticks)
{
    sim_task* me = self();
    if (!me->notify_pending) {
        me->notify_value &= ~clear_on_entry;
    }

    if (!wait_for(me, ticks, [me]() { return me->notify_pending; })) {
        if (value != nullptr) {
            *value = me->notify_value;
        }
        return pdFALSE;
    }

    if (value != nullptr) {
        *value = me->notify_value;
    }
    me->notify_value &= ~clear_on_exit;
    me->notify_pending = false;
    return pdTRUE;
}

QueueHandle_t xQueueCreate(UBaseType_t length, UBaseType_t item_size)
{
    return length == 0 ? nullptr : new_queue(QueueKind::Queue, length, item_size, 0);
}

BaseType_t xQueueSendToBack(QueueHandle_t queue, const void* item, TickType_t ticks) { return queue_send(queue, item, ticks, false); }

BaseType_t xQueueSendToFront(QueueHandle_t queue, const void* item, TickType_t ticks) { return queue_send(queue, item, ticks, true); }

BaseType_t xQueueOverwrite(QueueHandle_t queue, const void* item)
{
    if (queue == nullptr) {
        return pdFAIL;
    }
    queue->count = 0;
    queue->head = 0;
    return queue_send(queue, item, 0, false);
}

BaseType_t xQueueReceive(QueueHandle_t queue, void* buffer, TickType_t ticks) { return queue_receive(queue, buffer, ticks, false); }

BaseType_t xQueuePeek(QueueHandle_t queue, void* buffer, TickType_t ticks) { return queue_receive(queue, buffer, ticks, true); }

UBaseType_t uxQueueMessagesWaiting(QueueHandle_t queue) { return queue != nullptr ? queue->count : 0; }

UBaseType_t uxQueueSpacesAvailable(QueueHandle_t queue) { return queue != nullptr ? queue->length - queue->count : 0; }

BaseType_t xQueueReset(QueueHandle_t queue)
{
    if (queue != nullptr) {
        queue->count = 0;
        queue->head = 0;
        wake_waiters(queue);
    }
    return pdPASS;
}

void vQueueDelete(QueueHandle_t queue) { delete queue; }

SemaphoreHandle_t xSemaphoreCreateBinary(void) { return new_queue(QueueKind::Binary, 1, 0, 0); }

SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t max_count, UBaseType_t initial_count)
{
    return max_count == 0 ? nullptr : new_queue(QueueKind::Counting, max_count, 0, initial_count < max_count ? initial_count : max_count);
}

SemaphoreHandle_t xSemaphoreCreateMutex(void) { return new_queue(QueueKind::Mutex, 1, 0, 1); }

SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void) { return new_queue(QueueKind::RecursiveMutex, 1, 0, 1); }

BaseType_t xSemaphoreTake(SemaphoreHandle_t semaphore, TickType_t ticks)
{
    if (semaphore != nullptr && semaphore->kind == QueueKind::RecursiveMutex) {
        return xSemaphoreTakeRecursive(semaphore, ticks);
    }

    BaseType_t taken = queue_receive(semaphore, nullptr, ticks, false);
    if (taken == pdPASS && semaphore->kind == QueueKind::Mutex) {
        semaphore->holder = self();
    }
    return taken;
}

BaseType_t xSemaphoreGive(SemaphoreHandle_t semaphore)
{
    if (semaphore == nullptr) {
        return pdFAIL;
    }
    if (semaphore->kind == QueueKind::RecursiveMutex) {
        return xSemaphoreGiveRecursive(semaphore);
    }
    if (semaphore->kind == QueueKind::Mutex) {
        if (semaphore->holder != self()) {
            return pdFAIL;
        }
        semaphore->holder = nullptr;
    }
    return queue_send(semaphore, nullptr, 0, false);
}

BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t mutex, TickType_t ticks)
{
    if (mutex == nullptr) {
        return pdFAIL;
    }

    sim_task* me = self();
    if (mutex->holder == me) {
        mutex->recursion++;
        return pdPASS;
    }

    if (queue_receive(mutex, nullptr, ticks, false) != pdPASS) {
        return pdFAIL;
    }
    mutex->holder = me;
    mutex->recursion = 1;
    return pdPASS;
}

BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t mutex)
{
    if (mutex == nullptr || mutex->holder != self()) {
        return pdFAIL;
    }
    if (--mutex->recursion > 0) {
        return pdPASS;
    }
    mutex->holder = nullptr;
    return queue_send(mutex, nullptr, 0, false);
}

UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t semaphore) { return semaphore != nullptr ? semaphore->count : 0; }

TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t mutex) { return mutex != nullptr ? mutex->holder : nullptr; }

void vSemaphoreDelete(SemaphoreHandle_t semaphore) { delete semaphore; }

void sim_critical_enter(void)
{
    if (scheduler().critical_nesting++ == 0) {
        host().set_interrupts_enabled(0);
    }
}

void sim_critical_exit(void)
{
    Scheduler& s = scheduler();
    if (s.critical_nesting > 0 && --s.critical_nesting == 0) {
        host().set_interrupts_enabled(1);
    }
}

}  // extern "C"
