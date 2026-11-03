/* WiFiUdp.cpp - see WiFiUdp.h. */
#include "WiFiUdp.h"

#include <stdio.h>
#include <string.h>

#include <sim/host.h>
#include <sim/network_wait.h>

using sim::host;

uint8_t WiFiUDP::begin(uint16_t port)
{
    stop();
    int32_t handle = host().udp_open(port);
    handle_ = handle > 0 ? handle : 0;
    return handle_ != 0 ? 1 : 0;
}

bool WiFiUDP::ensure_open() { return handle_ != 0 || begin(0) == 1; }

void WiFiUDP::stop()
{
    if (handle_ != 0) {
        host().net_close(handle_);
        handle_ = 0;
    }
}

int WiFiUDP::beginPacket(const char* host_name, uint16_t port)
{
    if (host_name == nullptr || !ensure_open()) {
        return 0;
    }
    snprintf(target_, sizeof target_, "%s", host_name);
    target_port_ = port;
    tx_length_ = 0;
    return 1;
}

size_t WiFiUDP::write(const uint8_t* buffer, size_t size)
{
    size_t room = static_cast<size_t>(BufferSize - tx_length_);
    size_t count = size < room ? size : room;
    memcpy(tx_ + tx_length_, buffer, count);
    tx_length_ += static_cast<int>(count);
    return count;
}

int WiFiUDP::endPacket()
{
    if (handle_ == 0 || target_[0] == '\0') {
        return 0;
    }
    int sent = host().udp_send(handle_, target_, target_port_, tx_, tx_length_);
    tx_length_ = 0;
    return sent;
}

int WiFiUDP::parsePacket()
{
    if (handle_ == 0) {
        return 0;
    }
    int32_t length = host().udp_receive(handle_, rx_, BufferSize, &remote_ip_, &remote_port_);
    if (length < 0) {
        sim::network_wait();
        return 0;
    }
    rx_length_ = length < BufferSize ? length : BufferSize;
    rx_index_ = 0;
    return length;
}

int WiFiUDP::read(unsigned char* buffer, size_t len)
{
    int count = 0;
    while (static_cast<size_t>(count) < len && rx_index_ < rx_length_) {
        buffer[count++] = rx_[rx_index_++];
    }
    return count;
}