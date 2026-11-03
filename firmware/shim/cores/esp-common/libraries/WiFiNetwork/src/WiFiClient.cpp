/* WiFiClient.cpp - see WiFiClient.h. */
#include "WiFiClient.h"

#include <sim/host.h>
#include <sim/network_wait.h>

using sim::host;

namespace {

constexpr int32_t Connected = 3;  // wl_status_t WL_CONNECTED
constexpr int32_t SoftApAddress = 4;

bool network_ready()
{
    if (host().wifi_status() == Connected || host().wifi_address(SoftApAddress) != 0) {
        return true;
    }
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("WiFiClient.connect(): Wi-Fi is not connected, so the board has no network; the connection fails.");
    }
    return false;
}

}  // namespace

WiFiClient::WiFiClient(int32_t handle) : shared_(new Shared{handle, 1, -1}) {}

WiFiClient::WiFiClient(const WiFiClient& other) : Client(), shared_(other.shared_), timeout_ms_(other.timeout_ms_)
{
    if (shared_ != nullptr) {
        shared_->refs++;
    }
}

WiFiClient& WiFiClient::operator=(const WiFiClient& other)
{
    if (this != &other) {
        release();
        shared_ = other.shared_;
        timeout_ms_ = other.timeout_ms_;
        if (shared_ != nullptr) {
            shared_->refs++;
        }
    }
    return *this;
}

WiFiClient::~WiFiClient() { release(); }

void WiFiClient::release()
{
    if (shared_ != nullptr && --shared_->refs == 0) {
        host().net_close(shared_->handle);
        delete shared_;
    }
    shared_ = nullptr;
}

int32_t WiFiClient::handle() const { return shared_ != nullptr ? shared_->handle : 0; }

int WiFiClient::open(const char* host_name, uint16_t port, int32_t timeout_ms, int32_t tls, const char* ca_pem)
{
    stop();
    if (host_name == nullptr || !network_ready()) {
        return 0;
    }
    int32_t h = host().net_connect(host_name, port, tls, ca_pem);
    shared_ = new Shared{h, 1, -1};
    unsigned long start = millis();
    for (;;) {
        int32_t state = host().net_state(h);
        if (state == 1) {
            return 1;
        }
        if (state < 0 || state == 2 || millis() - start >= static_cast<unsigned long>(timeout_ms)) {
            stop();
            return 0;
        }
        delay(1);
    }
}

int WiFiClient::connect(const char* host_name, uint16_t port, int32_t timeout_ms) { return open(host_name, port, timeout_ms, 0, nullptr); }

size_t WiFiClient::write(const uint8_t* buf, size_t size)
{
    if (shared_ == nullptr || size == 0) {
        return 0;
    }
    return static_cast<size_t>(host().net_write(shared_->handle, buf, static_cast<int32_t>(size)));
}

size_t WiFiClient::write(Stream& stream)
{
    uint8_t buffer[256];
    size_t total = 0;
    while (stream.available() > 0) {
        size_t n = stream.readBytes(buffer, sizeof buffer);
        if (n == 0) {
            break;
        }
        total += write(buffer, n);
    }
    return total;
}

int WiFiClient::available()
{
    if (shared_ == nullptr) {
        return 0;
    }
    int count = host().net_available(shared_->handle) + (shared_->peeked >= 0 ? 1 : 0);
    if (count == 0) {
        // Waiting for data: other tasks get their turn, as they would on two cores.
        sim::network_wait();
    }
    return count;
}

bool WiFiClient::fill()
{
    if (shared_ == nullptr) {
        return false;
    }
    if (shared_->peeked >= 0) {
        return true;
    }
    uint8_t byte;
    if (host().net_read(shared_->handle, &byte, 1) == 1) {
        shared_->peeked = byte;
        return true;
    }
    sim::network_wait();
    return false;
}

int WiFiClient::read()
{
    if (!fill()) {
        return -1;
    }
    int value = shared_->peeked;
    shared_->peeked = -1;
    return value;
}

int WiFiClient::read(uint8_t* buf, size_t size)
{
    if (shared_ == nullptr || size == 0) {
        return 0;
    }
    size_t count = 0;
    if (shared_->peeked >= 0) {
        buf[count++] = static_cast<uint8_t>(shared_->peeked);
        shared_->peeked = -1;
    }
    if (count < size) {
        count += static_cast<size_t>(host().net_read(shared_->handle, buf + count, static_cast<int32_t>(size - count)));
    }
    return count == 0 && connected() == 0 ? -1 : static_cast<int>(count);
}

int WiFiClient::peek() { return fill() ? shared_->peeked : -1; }

void WiFiClient::stop()
{
    if (shared_ != nullptr) {
        host().net_close(shared_->handle);
        shared_->handle = 0;
    }
    release();
}

uint8_t WiFiClient::connected()
{
    if (shared_ == nullptr) {
        return 0;
    }
    int32_t state = host().net_state(shared_->handle);
    return state == 1 || (state == 2 && available() > 0) ? 1 : 0;
}

IPAddress WiFiClient::remoteIP()
{
    uint32_t ip = 0;
    uint16_t port = 0;
    return shared_ != nullptr && host().net_remote(shared_->handle, &ip, &port) == 1 ? IPAddress(ip) : IPAddress();
}

uint16_t WiFiClient::remotePort()
{
    uint32_t ip = 0;
    uint16_t port = 0;
    return shared_ != nullptr && host().net_remote(shared_->handle, &ip, &port) == 1 ? port : 0;
}

IPAddress WiFiClient::localIP() { return IPAddress(host().wifi_address(0)); }