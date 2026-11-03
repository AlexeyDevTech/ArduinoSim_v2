/*
 * ESP8266WiFi.h - the ESP8266 core's WiFi API (core 3.x), written for ArduSim (ADR-010). The radio
 * is simulated as on the ESP32 (networks from the project, scan about 2 s, connect about 1 s);
 * WiFiClient, WiFiServer and WiFiUDP use the computer's real network. Event handlers
 * (onStationModeGotIP, ...) run between two loop() calls and in delay(), as the SDK runs them,
 * and only while the sketch keeps the returned WiFiEventHandler.
 */
#ifndef ESP8266WiFi_h
#define ESP8266WiFi_h

#include <Arduino.h>

#include <functional>
#include <memory>

#include "IPAddress.h"
#include "WiFiClient.h"
#include "WiFiClientSecure.h"
#include "WiFiServer.h"
#include "WiFiUdp.h"

typedef enum {
    WL_NO_SHIELD = 255,
    WL_IDLE_STATUS = 0,
    WL_NO_SSID_AVAIL = 1,
    WL_SCAN_COMPLETED = 2,
    WL_CONNECTED = 3,
    WL_CONNECT_FAILED = 4,
    WL_CONNECTION_LOST = 5,
    WL_WRONG_PASSWORD = 6,
    WL_DISCONNECTED = 7
} wl_status_t;

typedef enum { WIFI_OFF = 0, WIFI_STA = 1, WIFI_AP = 2, WIFI_AP_STA = 3 } WiFiMode_t;
typedef enum { WIFI_PHY_MODE_11B = 1, WIFI_PHY_MODE_11G = 2, WIFI_PHY_MODE_11N = 3 } WiFiPhyMode_t;
typedef enum { WIFI_NONE_SLEEP = 0, WIFI_LIGHT_SLEEP = 1, WIFI_MODEM_SLEEP = 2 } WiFiSleepType_t;

enum wl_enc_type { ENC_TYPE_WEP = 5, ENC_TYPE_TKIP = 2, ENC_TYPE_CCMP = 4, ENC_TYPE_NONE = 7, ENC_TYPE_AUTO = 8 };

#define WIFI_SCAN_RUNNING (-1)
#define WIFI_SCAN_FAILED (-2)

struct WiFiEventStationModeConnected {
    String ssid;
    uint8_t bssid[6];
    uint8_t channel;
};

struct WiFiEventStationModeDisconnected {
    String ssid;
    uint8_t bssid[6];
    uint8_t reason;
};

struct WiFiEventStationModeGotIP {
    IPAddress ip;
    IPAddress mask;
    IPAddress gw;
};

using WiFiEventHandler = std::shared_ptr<int>;

class ESP8266WiFiClass {
public:
    wl_status_t begin(const char* ssid, const char* passphrase = nullptr, int32_t channel = 0, const uint8_t* bssid = nullptr, bool connect = true);
    wl_status_t begin(const String& ssid, const String& passphrase = String()) { return begin(ssid.c_str(), passphrase.c_str()); }
    wl_status_t begin();
    bool config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1 = IPAddress(), IPAddress dns2 = IPAddress());
    bool disconnect(bool wifioff = false);
    bool reconnect();
    bool isConnected() { return status() == WL_CONNECTED; }
    bool setAutoConnect(bool) { return true; }
    bool setAutoReconnect(bool autoReconnect)
    {
        autoReconnect_ = autoReconnect;
        return true;
    }
    bool getAutoReconnect() { return autoReconnect_; }
    int8_t waitForConnectResult(unsigned long timeoutLength = 60000);
    wl_status_t status();

    IPAddress localIP();
    IPAddress gatewayIP();
    IPAddress subnetMask();
    IPAddress dnsIP(uint8_t dns_no = 0);
    IPAddress broadcastIP();
    String macAddress();
    uint8_t* macAddress(uint8_t* mac);
    String SSID() const { return String(ssid_); }
    String psk() const { return String(password_); }
    int32_t RSSI();
    int32_t channel() { return 6; }
    String hostname() { return String(hostname_); }
    bool hostname(const char* name);
    bool hostname(const String& name) { return hostname(name.c_str()); }
    bool setHostname(const char* name) { return hostname(name); }
    const char* getHostname() { return hostname_; }

    bool mode(WiFiMode_t mode);
    WiFiMode_t getMode() { return mode_; }
    bool enableSTA(bool enable) { return mode(enable ? (mode_ == WIFI_AP ? WIFI_AP_STA : WIFI_STA) : (mode_ == WIFI_AP_STA ? WIFI_AP : WIFI_OFF)); }
    bool enableAP(bool enable) { return mode(enable ? (mode_ == WIFI_STA ? WIFI_AP_STA : WIFI_AP) : (mode_ == WIFI_AP_STA ? WIFI_STA : WIFI_OFF)); }
    void persistent(bool) {}
    bool setSleepMode(WiFiSleepType_t, uint8_t = 0) { return true; }
    bool setPhyMode(WiFiPhyMode_t) { return true; }
    void setOutputPower(float) {}
    bool forceSleepBegin(uint32_t = 0) { return mode(WIFI_OFF); }
    bool forceSleepWake() { return true; }

    int8_t scanNetworks(bool async = false, bool show_hidden = false, uint8_t channel = 0, uint8_t* ssid = nullptr);
    int8_t scanComplete();
    void scanDelete() {}
    String SSID(uint8_t networkItem);
    int32_t RSSI(uint8_t networkItem);
    uint8_t encryptionType(uint8_t networkItem);
    int32_t channel(uint8_t networkItem);
    bool isHidden(uint8_t) { return false; }
    String BSSIDstr(uint8_t networkItem);

    bool softAP(const char* ssid, const char* passphrase = nullptr, int channel = 1, int ssid_hidden = 0, int max_connection = 4);
    bool softAP(const String& ssid, const String& passphrase = String()) { return softAP(ssid.c_str(), passphrase.c_str()); }
    bool softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet);
    IPAddress softAPIP();
    uint8_t softAPgetStationNum() { return 0; }
    bool softAPdisconnect(bool wifioff = false);
    String softAPmacAddress() { return String("C2:FF:EE:00:00:02"); }

    int hostByName(const char* aHostname, IPAddress& aResult);
    int hostByName(const char* aHostname, IPAddress& aResult, uint32_t timeout_ms);

    WiFiEventHandler onStationModeConnected(std::function<void(const WiFiEventStationModeConnected&)> handler);
    WiFiEventHandler onStationModeDisconnected(std::function<void(const WiFiEventStationModeDisconnected&)> handler);
    WiFiEventHandler onStationModeGotIP(std::function<void(const WiFiEventStationModeGotIP&)> handler);

    /// Runs the event handlers once the radio changed; called between loop() calls and in delay().
    void pollEvents();

private:
    char ssid_[33] = {};
    char password_[65] = {};
    char hostname_[33] = "ESP-C0FFEE";
    WiFiMode_t mode_ = WIFI_STA;
    bool autoReconnect_ = true;
    bool staticIp_ = false;
    IPAddress local_;
    IPAddress gateway_;
    IPAddress subnet_;
    IPAddress dns_;
};

extern ESP8266WiFiClass WiFi;

#endif