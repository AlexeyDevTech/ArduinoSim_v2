/*
 * freertos/FreeRTOS.h - the FreeRTOS API of the Arduino-ESP32 core as cooperative tasks
 * (docs/adr/ADR-009-esp-boards.md). Every task is an OS thread, but exactly one runs at a time:
 * a task gives up the CPU in vTaskDelay, delay, yield and while it waits for a queue, semaphore
 * or notification, and the task that is due first runs next. There is no preemption; the core
 * number is accepted and ignored; priorities only order tasks that are due at the same time.
 * task.h, queue.h and semphr.h include this header, as they do on the real core.
 */
#ifndef SIM_FREERTOS_H
#define SIM_FREERTOS_H

#include <stddef.h>
#include <stdint.h>

#define SIM_RTOS_UNAVAILABLE(what) __attribute__((unavailable("ArduSim's cooperative FreeRTOS does not simulate " what)))

typedef uint32_t TickType_t;
typedef int32_t BaseType_t;
typedef uint32_t UBaseType_t;
typedef uint8_t StackType_t;
typedef void (*TaskFunction_t)(void*);
typedef struct sim_task* TaskHandle_t;
typedef struct sim_queue* QueueHandle_t;
typedef QueueHandle_t SemaphoreHandle_t;
typedef struct {
    int unused;
} portMUX_TYPE;
typedef portMUX_TYPE spinlock_t;

typedef enum { eRunning, eReady, eBlocked, eSuspended, eDeleted, eInvalid } eTaskState;
typedef enum { eNoAction, eSetBits, eIncrement, eSetValueWithOverwrite, eSetValueWithoutOverwrite } eNotifyAction;

#define pdFALSE ((BaseType_t)0)
#define pdTRUE ((BaseType_t)1)
#define pdFAIL pdFALSE
#define pdPASS pdTRUE
#define errQUEUE_EMPTY ((BaseType_t)0)
#define errQUEUE_FULL ((BaseType_t)0)
#define portMAX_DELAY ((TickType_t)0xFFFFFFFFUL)
#define configTICK_RATE_HZ 1000
#define portTICK_PERIOD_MS ((TickType_t)1000 / configTICK_RATE_HZ)
#define portTICK_RATE_MS portTICK_PERIOD_MS
#define pdMS_TO_TICKS(ms) ((TickType_t)(((TickType_t)(ms) * (TickType_t)configTICK_RATE_HZ) / (TickType_t)1000U))
#define pdTICKS_TO_MS(ticks) ((TickType_t)(((uint64_t)(ticks) * 1000U) / configTICK_RATE_HZ))
#define configMAX_PRIORITIES 25
#define configMINIMAL_STACK_SIZE 768
#define tskIDLE_PRIORITY ((UBaseType_t)0)
#define tskNO_AFFINITY ((BaseType_t)0x7FFFFFFF)
#define portNUM_PROCESSORS 2
#define ARDUINO_RUNNING_CORE 1
#define CONFIG_ARDUINO_RUNNING_CORE 1
#define ARDUINO_EVENT_RUNNING_CORE 1
#define CONFIG_FREERTOS_HZ configTICK_RATE_HZ
#define portMUX_INITIALIZER_UNLOCKED {0}
#define SPINLOCK_INITIALIZER portMUX_INITIALIZER_UNLOCKED

#ifdef __cplusplus
extern "C" {
#endif

/* Tasks */
BaseType_t xTaskCreate(TaskFunction_t code, const char* name, uint32_t stack_depth, void* parameters, UBaseType_t priority, TaskHandle_t* created);
BaseType_t xTaskCreatePinnedToCore(TaskFunction_t code, const char* name, uint32_t stack_depth, void* parameters, UBaseType_t priority, TaskHandle_t* created, BaseType_t core_id);
void vTaskDelete(TaskHandle_t task);
void vTaskDelay(TickType_t ticks);
BaseType_t xTaskDelayUntil(TickType_t* previous_wake, TickType_t increment);
TickType_t xTaskGetTickCount(void);
TickType_t xTaskGetTickCountFromISR(void);
void vTaskSuspend(TaskHandle_t task);
void vTaskResume(TaskHandle_t task);
BaseType_t xTaskResumeFromISR(TaskHandle_t task);
TaskHandle_t xTaskGetCurrentTaskHandle(void);
TaskHandle_t xTaskGetHandle(const char* name);
char* pcTaskGetName(TaskHandle_t task);
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t task);
UBaseType_t uxTaskPriorityGet(TaskHandle_t task);
void vTaskPrioritySet(TaskHandle_t task, UBaseType_t priority);
UBaseType_t uxTaskGetNumberOfTasks(void);
eTaskState eTaskGetState(TaskHandle_t task);
BaseType_t xPortGetCoreID(void);
BaseType_t xTaskGetAffinity(TaskHandle_t task);
void sim_task_yield(void);
void sim_task_switch_point(void); /* after each loop(): other due tasks run, if there are any */
void vTaskSuspendAll(void);
BaseType_t xTaskResumeAll(void);

/* Notifications */
BaseType_t xTaskNotify(TaskHandle_t task, uint32_t value, eNotifyAction action);
uint32_t ulTaskNotifyTake(BaseType_t clear_on_exit, TickType_t ticks);
BaseType_t xTaskNotifyWait(uint32_t clear_on_entry, uint32_t clear_on_exit, uint32_t* value, TickType_t ticks);

/* Queues */
QueueHandle_t xQueueCreate(UBaseType_t length, UBaseType_t item_size);
BaseType_t xQueueSendToBack(QueueHandle_t queue, const void* item, TickType_t ticks);
BaseType_t xQueueSendToFront(QueueHandle_t queue, const void* item, TickType_t ticks);
BaseType_t xQueueOverwrite(QueueHandle_t queue, const void* item);
BaseType_t xQueueReceive(QueueHandle_t queue, void* buffer, TickType_t ticks);
BaseType_t xQueuePeek(QueueHandle_t queue, void* buffer, TickType_t ticks);
UBaseType_t uxQueueMessagesWaiting(QueueHandle_t queue);
UBaseType_t uxQueueSpacesAvailable(QueueHandle_t queue);
BaseType_t xQueueReset(QueueHandle_t queue);
void vQueueDelete(QueueHandle_t queue);

/* Semaphores and mutexes */
SemaphoreHandle_t xSemaphoreCreateBinary(void);
SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t max_count, UBaseType_t initial_count);
SemaphoreHandle_t xSemaphoreCreateMutex(void);
SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void);
BaseType_t xSemaphoreTake(SemaphoreHandle_t semaphore, TickType_t ticks);
BaseType_t xSemaphoreGive(SemaphoreHandle_t semaphore);
BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t mutex, TickType_t ticks);
BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t mutex);
UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t semaphore);
TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t mutex);
void vSemaphoreDelete(SemaphoreHandle_t semaphore);

/* Critical sections: tasks never preempt each other, so only interrupts are masked. */
void sim_critical_enter(void);
void sim_critical_exit(void);

/* Not simulated: fail at compile time with a message. */
typedef struct sim_timer* TimerHandle_t;
typedef void (*TimerCallbackFunction_t)(TimerHandle_t);
TimerHandle_t xTimerCreate(const char* name, TickType_t period, UBaseType_t auto_reload, void* id, TimerCallbackFunction_t callback)
    SIM_RTOS_UNAVAILABLE("software timers (xTimerCreate); use a task with vTaskDelay");
typedef struct sim_event_group* EventGroupHandle_t;
EventGroupHandle_t xEventGroupCreate(void) SIM_RTOS_UNAVAILABLE("event groups (xEventGroupCreate); use a queue or a semaphore");
typedef struct sim_stream_buffer* StreamBufferHandle_t;
StreamBufferHandle_t xStreamBufferGenericCreate(size_t size, size_t trigger, BaseType_t message_buffer, void* a, void* b)
    SIM_RTOS_UNAVAILABLE("stream and message buffers");

#ifdef __cplusplus
}
#endif

#define vTaskDelayUntil(previous_wake, increment) ((void)xTaskDelayUntil((previous_wake), (increment)))
#define pcTaskGetTaskName pcTaskGetName
#define taskYIELD() sim_task_yield()
#define portYIELD() sim_task_yield()
#define portYIELD_FROM_ISR(...) ((void)0)
#define portEND_SWITCHING_ISR(...) ((void)0)
#define xTaskNotifyGive(task) xTaskNotify((task), 0, eIncrement)
#define xTaskNotifyFromISR(task, value, action, woken) xTaskNotify((task), (value), (action))
#define vTaskNotifyGiveFromISR(task, woken) ((void)xTaskNotify((task), 0, eIncrement))
#define xQueueSend(queue, item, ticks) xQueueSendToBack((queue), (item), (ticks))
#define xQueueSendFromISR(queue, item, woken) xQueueSendToBack((queue), (item), 0)
#define xQueueSendToBackFromISR(queue, item, woken) xQueueSendToBack((queue), (item), 0)
#define xQueueSendToFrontFromISR(queue, item, woken) xQueueSendToFront((queue), (item), 0)
#define xQueueOverwriteFromISR(queue, item, woken) xQueueOverwrite((queue), (item))
#define xQueueReceiveFromISR(queue, buffer, woken) xQueueReceive((queue), (buffer), 0)
#define uxQueueMessagesWaitingFromISR(queue) uxQueueMessagesWaiting(queue)
#define xSemaphoreGiveFromISR(semaphore, woken) xSemaphoreGive(semaphore)
#define xSemaphoreTakeFromISR(semaphore, woken) xSemaphoreTake((semaphore), 0)
#define portENTER_CRITICAL(mux) sim_critical_enter()
#define portEXIT_CRITICAL(mux) sim_critical_exit()
#define portENTER_CRITICAL_ISR(mux) sim_critical_enter()
#define portEXIT_CRITICAL_ISR(mux) sim_critical_exit()
#define portENTER_CRITICAL_SAFE(mux) sim_critical_enter()
#define portEXIT_CRITICAL_SAFE(mux) sim_critical_exit()
#define taskENTER_CRITICAL(mux) sim_critical_enter()
#define taskEXIT_CRITICAL(mux) sim_critical_exit()
#define taskENTER_CRITICAL_ISR(mux) sim_critical_enter()
#define taskEXIT_CRITICAL_ISR(mux) sim_critical_exit()
#define portDISABLE_INTERRUPTS() sim_critical_enter()
#define portENABLE_INTERRUPTS() sim_critical_exit()
#define vPortCPUInitializeMutex(mux) ((void)(mux))
#define spinlock_initialize(lock) ((void)(lock))

#endif /* SIM_FREERTOS_H */
