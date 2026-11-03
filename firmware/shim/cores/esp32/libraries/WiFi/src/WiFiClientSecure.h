/*
 * WiFiClientSecure.h - TLS over the real network; the handshake and the certificate check are
 * done by the computer (ADR-010). setInsecure() skips the check, setCACert() checks against the
 * given PEM, otherwise the Windows certificate store decides. Client certificates (mutual TLS)
 * and pre-shared keys are not simulated.
 */
#ifndef WiFiClientSecure_h
#define WiFiClientSecure_h

#include "WiFiClient.h"

class WiFiClientSecure : public WiFiClient {
public:
    WiFiClientSecure() = default;

    int connect(IPAddress ip, uint16_t port) override { return connect(ip.toString().c_str(), port); }
    int connect(const char* host, uint16_t port) override { return open(host, port, 10000, insecure_ ? 2 : 1, ca_); }
    int connect(const char* host, uint16_t port, int32_t timeout_ms) { return open(host, port, timeout_ms, insecure_ ? 2 : 1, ca_); }

    void setInsecure() { insecure_ = true; }
    void setCACert(const char* rootCA)
    {
        ca_ = rootCA;
        insecure_ = false;
    }
    void setCertificate(const char* client_ca);
    void setPrivateKey(const char* private_key);
    void setHandshakeTimeout(unsigned long) {}
    bool verify(const char*, const char*) { return true; }

private:
    const char* ca_ = nullptr;
    bool insecure_ = false;
};

#endif