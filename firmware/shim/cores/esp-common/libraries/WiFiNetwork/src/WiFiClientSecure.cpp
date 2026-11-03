/* WiFiClientSecure.cpp - see WiFiClientSecure.h. */
#include "WiFiClientSecure.h"

#include <sim/host.h>

void WiFiClientSecure::setCertificate(const char*)
{
    sim::log_warning("WiFiClientSecure.setCertificate(): client certificates are not simulated; the server sees no client certificate.");
}

void WiFiClientSecure::setPrivateKey(const char*)
{
    sim::log_warning("WiFiClientSecure.setPrivateKey(): client certificates are not simulated; the key is ignored.");
}

#if defined(ARDUINO_ARCH_ESP8266)
bool WiFiClientSecure::setFingerprint(const char*)
{
    sim::log_warning("WiFiClientSecure.setFingerprint(): fingerprints are not checked; the connection is made without verifying the server. Use setTrustAnchors() to verify against a certificate.");
    setInsecure();
    return true;
}

void WiFiClientSecure::setTrustAnchors(const X509List* anchors) { setCACert(anchors != nullptr ? anchors->pem() : nullptr); }
#endif