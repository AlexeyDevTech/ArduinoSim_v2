/*
 * WiFiClient.h - a TCP connection over the computer's real network (ADR-010). Copies share the
 * connection; it closes on stop() or when the last copy goes away, as on the ESP32 core.
 */
#ifndef WiFiClient_h
#define WiFiClient_h

#include <Arduino.h>

#include "Client.h"
#include "IPAddress.h"

class WiFiClient : public Client {
public:
    WiFiClient() = default;
    explicit WiFiClient(int32_t handle);
    WiFiClient(const WiFiClient& other);
    WiFiClient& operator=(const WiFiClient& other);
    virtual ~WiFiClient();

    int connect(IPAddress ip, uint16_t port) override { return connect(ip.toString().c_str(), port); }
    int connect(IPAddress ip, uint16_t port, int32_t timeout_ms) { return connect(ip.toString().c_str(), port, timeout_ms); }
    int connect(const char* host, uint16_t port) override { return connect(host, port, timeout_ms_); }
    int connect(const char* host, uint16_t port, int32_t timeout_ms);
    size_t write(uint8_t data) override { return write(&data, 1); }
    size_t write(const uint8_t* buf, size_t size) override;
    size_t write_P(PGM_P buf, size_t size) { return write(reinterpret_cast<const uint8_t*>(buf), size); }
    size_t write(Stream& stream);
    int available() override;
    int read() override;
    int read(uint8_t* buf, size_t size) override;
    int peek() override;
    void flush() override {}
    void stop() override;
    uint8_t connected() override;
    operator bool() override { return connected() != 0; }
    bool operator==(const WiFiClient& other) const { return shared_ == other.shared_; }
    bool operator!=(const WiFiClient& other) const { return shared_ != other.shared_; }

    IPAddress remoteIP();
    uint16_t remotePort();
    IPAddress localIP();
    uint16_t localPort() { return 0; }
    int setTimeout(uint32_t seconds)
    {
        timeout_ms_ = static_cast<int32_t>(seconds * 1000);
        Stream::setTimeout(seconds * 1000);
        return 0;
    }
    int setNoDelay(bool) { return 0; }
    int fd() const { return handle(); }

    using Print::write;

protected:
    /// tls: 0 none, 1 verify against ca_pem (or the Windows store when null), 2 insecure.
    int open(const char* host, uint16_t port, int32_t timeout_ms, int32_t tls, const char* ca_pem);
    int32_t handle() const;

private:
    struct Shared {
        int32_t handle;
        int refs;
        int peeked;
    };

    void release();
    bool fill();

    Shared* shared_ = nullptr;
    int32_t timeout_ms_ = 3000;
};

#endif