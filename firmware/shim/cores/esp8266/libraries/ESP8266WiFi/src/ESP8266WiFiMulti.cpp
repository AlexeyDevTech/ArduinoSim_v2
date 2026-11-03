/* ESP8266WiFiMulti.cpp - see ESP8266WiFiMulti.h. */
#include "ESP8266WiFiMulti.h"

bool ESP8266WiFiMulti::addAP(const char* ssid, const char* passphrase)
{
    if (ssid == nullptr || ssid[0] == '\0' || existsAP(ssid, passphrase)) {
        return false;
    }
    aps_.push_back(AccessPoint{String(ssid), String(passphrase != nullptr ? passphrase : "")});
    return true;
}

bool ESP8266WiFiMulti::existsAP(const char* ssid, const char* passphrase)
{
    for (const AccessPoint& ap : aps_) {
        if (ap.ssid == ssid && ap.passphrase == (passphrase != nullptr ? passphrase : "")) {
            return true;
        }
    }
    return false;
}

// As the core: when not connected, scan, pick the known network with the best signal and join it.
wl_status_t ESP8266WiFiMulti::run(uint32_t connectTimeoutMs)
{
    if (WiFi.status() == WL_CONNECTED) {
        return WL_CONNECTED;
    }

    int8_t found = WiFi.scanNetworks();
    const AccessPoint* best = nullptr;
    int32_t bestRssi = -1000;
    for (int8_t i = 0; i < found; i++) {
        for (const AccessPoint& ap : aps_) {
            if (WiFi.SSID(static_cast<uint8_t>(i)) == ap.ssid && WiFi.RSSI(static_cast<uint8_t>(i)) > bestRssi) {
                best = &ap;
                bestRssi = WiFi.RSSI(static_cast<uint8_t>(i));
            }
        }
    }
    if (best == nullptr) {
        return WL_NO_SSID_AVAIL;
    }

    WiFi.begin(best->ssid.c_str(), best->passphrase.c_str());
    int8_t result = WiFi.waitForConnectResult(connectTimeoutMs);
    return result < 0 ? WL_DISCONNECTED : static_cast<wl_status_t>(result);
}