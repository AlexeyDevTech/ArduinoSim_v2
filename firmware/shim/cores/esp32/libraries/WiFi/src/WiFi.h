/*
 * WiFi.h - the Arduino-ESP32 WiFi API (core 3.x), written for ArduSim (docs/adr/ADR-010-network.md).
 * The radio is simulated: the networks the board sees are set in the board's properties, a scan
 * takes about 2 s and a connection about 1 s of virtual time. Once connected, WiFiClient,
 * WiFiServer, WiFiUDP and HTTPClient use the computer's real network; servers listen on
 * 127.0.0.1 only. Events (onEvent) fire from a task that polls the radio every 10 ms.
 */
#ifndef WiFi_h
#define WiFi_h

#include <Arduino.h>

#include "IPAddress.h"
#include "WiFiClient.h"
#include "WiFiServer.h"
#include "WiFiUdp.h"

typedef enum {
    WL_NO_SHIELD = 255,
    WL_STOPPED = 254,
    WL_IDLE_STATUS = 0,
    WL_NO_SSID_AVAIL = 1,
    WL_SCAN_COMPLETED = 2,
    WL_CONNECTED = 3,
    WL_CONNECT_FAILED = 4,
    WL_CONNECTION_LOST = 5,
    WL_DISCONNECTED = 6
} wl_status_t;

typedef enum { WIFI_MODE_NULL = 0, WIFI_MODE_STA, WIFI_MODE_AP, WIFI_MODE_APSTA, WIFI_MODE_MAX } wifi_mode_t;
#define WIFI_OFF WIFI_MODE_NULL
#define WIFI_STA WIFI_MODE_STA
#define WIFI_AP WIFI_MODE_AP
#define WIFI_AP_STA WIFI_MODE_APSTA

typedef enum {
    WIFI_AUTH_OPEN = 0,
    WIFI_AUTH_WEP,
    WIFI_AUTH_WPA_PSK,
    WIFI_AUTH_WPA2_PSK,
    WIFI_AUTH_WPA_WPA2_PSK,
    WIFI_AUTH_WPA2_ENTERPRISE,
    WIFI_AUTH_WPA3_PSK,
    WIFI_AUTH_WPA2_WPA3_PSK,
    WIFI_AUTH_MAX
} wifi_auth_mode_t;

typedef enum {
    WIFI_POWER_19_5dBm = 78,
    WIFI_POWER_11dBm = 44,
    WIFI_POWER_8_5dBm = 34,
    WIFI_POWER_2dBm = 8,
    WIFI_POWER_MINUS_1dBm = -4
} wifi_power_t;

#define WIFI_SCAN_RUNNING (-1)
#define WIFI_SCAN_FAILED (-2)

typedef enum {
    ARDUINO_EVENT_NONE = 0,
    ARDUINO_EVENT_WIFI_READY,
    ARDUINO_EVENT_WIFI_SCAN_DONE,
    ARDUINO_EVENT_WIFI_STA_START,
    ARDUINO_EVENT_WIFI_STA_STOP,
    ARDUINO_EVENT_WIFI_STA_CONNECTED,
    ARDUINO_EVENT_WIFI_STA_DISCONNECTED,
    ARDUINO_EVENT_WIFI_STA_AUTHMODE_CHANGE,
    ARDUINO_EVENT_WIFI_STA_GOT_IP,
    ARDUINO_EVENT_WIFI_STA_GOT_IP6,
    ARDUINO_EVENT_WIFI_STA_LOST_IP,
    ARDUINO_EVENT_WIFI_AP_START,
    ARDUINO_EVENT_WIFI_AP_STOP,
    ARDUINO_EVENT_WIFI_AP_STACONNECTED,
    ARDUINO_EVENT_WIFI_AP_STADISCONNECTED,
    ARDUINO_EVENT_WIFI_AP_STAIPASSIGNED,
    ARDUINO_EVENT_MAX
} arduino_event_id_t;

typedef union {
    struct {
        uint8_t reason;
    } wifi_sta_disconnected;
    struct {
        uint16_t number;
    } wifi_scan_done;
    struct {
        uint32_t ip;
    } got_ip;
} arduino_event_info_t;

typedef arduino_event_id_t WiFiEvent_t;
typedef arduino_event_info_t WiFiEventInfo_t;
typedef void (*WiFiEventCb)(arduino_event_id_t event);
typedef void (*WiFiEventFuncCb)(arduino_event_id_t event, arduino_event_info_t info);
typedef size_t wifi_event_id_t;

class WiFiClass {
public:
    wl_status_t begin(const char* ssid, const char* passphrase = nullptr, int32_t channel = 0, const uint8_t* bssid = nullptr, bool connect = true);
    wl_status_t begin(const String& ssid, const String& passphrase = String()) { return begin(ssid.c_str(), passphrase.c_str()); }
    bool config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1 = IPAddress(), IPAddress dns2 = IPAddress());
    bool disconnect(bool wifioff = false, bool eraseap = false);
    bool reconnect();
    bool isConnected() { return status() == WL_CONNECTED; }
    bool setAutoReconnect(bool autoReconnect)
    {
        autoReconnect_ = autoReconnect;
        return true;
    }
    bool getAutoReconnect() { return autoReconnect_; }
    uint8_t waitForConnectResult(unsigned long timeoutLength = 60000);
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
    int8_t RSSI();
    int32_t channel() { return 6; }

    bool mode(wifi_mode_t mode);
    wifi_mode_t getMode() { return mode_; }
    bool enableSTA(bool enable) { return mode(enable ? (mode_ == WIFI_AP ? WIFI_AP_STA : WIFI_STA) : (mode_ == WIFI_AP_STA ? WIFI_AP : WIFI_OFF)); }
    bool enableAP(bool enable) { return mode(enable ? (mode_ == WIFI_STA ? WIFI_AP_STA : WIFI_AP) : (mode_ == WIFI_AP_STA ? WIFI_STA : WIFI_OFF)); }
    bool setHostname(const char* hostname);
    const char* getHostname() { return hostname_; }
    bool setSleep(bool) { return true; }
    bool setTxPower(wifi_power_t) { return true; }
    void persistent(bool) {}

    int16_t scanNetworks(bool async = false, bool show_hidden = false, bool passive = false, uint32_t max_ms_per_chan = 300, uint8_t channel = 0,
                         const char* ssid = nullptr, const uint8_t* bssid = nullptr);
    int16_t scanComplete();
    void scanDelete();
    String SSID(uint8_t networkItem);
    int32_t RSSI(uint8_t networkItem);
    wifi_auth_mode_t encryptionType(uint8_t networkItem);
    int32_t channel(uint8_t networkItem);
    String BSSIDstr(uint8_t networkItem);

    bool softAP(const char* ssid, const char* passphrase = nullptr, int channel = 1, int ssid_hidden = 0, int max_connection = 4, bool ftm_responder = false);
    bool softAP(const String& ssid, const String& passphrase = String()) { return softAP(ssid.c_str(), passphrase.c_str()); }
    bool softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet);
    IPAddress softAPIP();
    uint8_t softAPgetStationNum() { return 0; }
    bool softAPdisconnect(bool wifioff = false);
    String softAPmacAddress();

    int hostByName(const char* aHostname, IPAddress& aResult);

    wifi_event_id_t onEvent(WiFiEventCb callback, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    wifi_event_id_t onEvent(WiFiEventFuncCb callback, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    void removeEvent(wifi_event_id_t id);

    /// The event task's poll: compares the radio with what it saw last time and fires events.
    void pollEvents();

private:
    void fire(arduino_event_id_t event, arduino_event_info_t info);
    void startEventTask();

    char ssid_[33] = {};
    char password_[65] = {};
    char hostname_[33] = "esp32-000001";
    wifi_mode_t mode_ = WIFI_OFF;
    bool autoReconnect_ = true;
    bool staticIp_ = false;
    IPAddress local_;
    IPAddress gateway_;
    IPAddress subnet_;
    IPAddress dns_;
    bool apUp_ = false;
};

extern WiFiClass WiFi;

#endif
