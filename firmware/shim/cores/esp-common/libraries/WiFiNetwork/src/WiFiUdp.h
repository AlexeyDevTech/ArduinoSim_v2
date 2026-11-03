/* WiFiUdp.h - UDP over the computer's real network (ADR-010). */
#ifndef WiFiUdp_h
#define WiFiUdp_h

#include <Arduino.h>

#include "IPAddress.h"
#include "Udp.h"

class WiFiUDP : public UDP {
public:
    WiFiUDP() = default;
    virtual ~WiFiUDP() { stop(); }

    uint8_t begin(uint16_t port) override;
    uint8_t begin(IPAddress, uint16_t port) { return begin(port); }
    uint8_t beginMulticast(IPAddress, uint16_t port) { return begin(port); }
    void stop() override;
    int beginPacket() { return target_[0] != '\0' ? 1 : 0; }
    int beginPacket(IPAddress ip, uint16_t port) override { return beginPacket(ip.toString().c_str(), port); }
    int beginPacket(const char* host, uint16_t port) override;
    int endPacket() override;
    size_t write(uint8_t data) override { return write(&data, 1); }
    size_t write(const uint8_t* buffer, size_t size) override;
    int parsePacket() override;
    int available() override { return rx_length_ - rx_index_; }
    int read() override { return rx_index_ < rx_length_ ? rx_[rx_index_++] : -1; }
    int read(unsigned char* buffer, size_t len) override;
    int read(char* buffer, size_t len) override { return read(reinterpret_cast<unsigned char*>(buffer), len); }
    int peek() override { return rx_index_ < rx_length_ ? rx_[rx_index_] : -1; }
    void flush() override { rx_index_ = rx_length_; }
    IPAddress remoteIP() override { return IPAddress(remote_ip_); }
    uint16_t remotePort() override { return remote_port_; }

    using Print::write;

private:
    static constexpr int BufferSize = 1460;

    bool ensure_open();

    int32_t handle_ = 0;
    char target_[64] = {};
    uint16_t target_port_ = 0;
    uint8_t tx_[BufferSize] = {};
    int tx_length_ = 0;
    uint8_t rx_[BufferSize] = {};
    int rx_length_ = 0;
    int rx_index_ = 0;
    uint32_t remote_ip_ = 0;
    uint16_t remote_port_ = 0;
};

#endif