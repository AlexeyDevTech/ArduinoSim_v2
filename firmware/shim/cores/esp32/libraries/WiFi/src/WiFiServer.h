/*
 * WiFiServer.h - a TCP server. It listens on the computer's 127.0.0.1 only (ADR-010), on the
 * asked port or the next free one; the log says the address to open in a browser.
 */
#ifndef WiFiServer_h
#define WiFiServer_h

#include <Arduino.h>

#include "IPAddress.h"
#include "Server.h"
#include "WiFiClient.h"

class WiFiServer : public Server {
public:
    explicit WiFiServer(uint16_t port = 80, uint8_t max_clients = 4) : port_(port) { (void)max_clients; }
    WiFiServer(const IPAddress&, uint16_t port = 80, uint8_t max_clients = 4) : port_(port) { (void)max_clients; }
    virtual ~WiFiServer() { end(); }

    void begin() override { begin(port_); }
    void begin(uint16_t port, int reuse_enable = 0);
    WiFiClient available() { return accept(); }
    WiFiClient accept();
    bool hasClient();
    void setNoDelay(bool) {}
    bool getNoDelay() { return false; }
    void end();
    void close() { end(); }
    void stop() { end(); }
    operator bool() { return listener_ > 0; }

    size_t write(uint8_t) override { return 0; }
    using Print::write;

private:
    uint16_t port_;
    int32_t listener_ = 0;
    int32_t waiting_ = 0;
};

#endif