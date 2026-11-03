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