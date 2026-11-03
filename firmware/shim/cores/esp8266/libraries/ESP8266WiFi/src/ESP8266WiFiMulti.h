/* ESP8266WiFiMulti.h - connect to the strongest of several known networks (ESP8266 core API). */
#ifndef ESP8266WiFiMulti_h
#define ESP8266WiFiMulti_h

#include "ESP8266WiFi.h"

#include <vector>

class ESP8266WiFiMulti {
public:
    bool addAP(const char* ssid, const char* passphrase = nullptr);
    bool existsAP(const char* ssid, const char* passphrase = nullptr);
    wl_status_t run(uint32_t connectTimeoutMs = 5000);
    void cleanAPlist() { aps_.clear(); }

private:
    struct AccessPoint {
        String ssid;
        String passphrase;
    };

    std::vector<AccessPoint> aps_;
};

#endif