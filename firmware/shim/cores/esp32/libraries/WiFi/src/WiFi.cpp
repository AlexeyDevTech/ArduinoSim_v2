/* WiFi.cpp - see WiFi.h. */
#include "WiFi.h"

#include <stdio.h>
#include <string.h>

#include <sim/host.h>

using sim::host;

WiFiClass WiFi;

namespace {

constexpr int MaxHandlers = 16;
constexpr TickType_t EventPeriod = 10;

struct Handler {
    WiFiEventCb plain;
    WiFiEventFuncCb with_info;
    arduino_event_id_t event;
};

Handler g_handlers[MaxHandlers];
bool g_event_task = false;
int32_t g_last_status = -1;
int32_t g_last_scan = -2;
bool g_last_ap = false;

void event_task(void*)
{
    for (;;) {
        WiFi.pollEvents();
        vTaskDelay(EventPeriod);
    }
}

}  // namespace

wl_status_t WiFiClass::begin(const char* ssid, const char* passphrase, int32_t, const uint8_t*, bool connect)
{
    if (ssid == nullptr || ssid[0] == '\0') {
        return WL_CONNECT_FAILED;
    }
    snprintf(ssid_, sizeof ssid_, "%s", ssid);
    snprintf(password_, sizeof password_, "%s", passphrase != nullptr ? passphrase : "");
    if (mode_ == WIFI_OFF || mode_ == WIFI_AP) {
        mode_ = mode_ == WIFI_AP ? WIFI_AP_STA : WIFI_STA;
    }
    if (connect) {
        host().wifi_begin(ssid_, password_);
    }
    return status();
}

bool WiFiClass::config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1, IPAddress)
{
    staticIp_ = static_cast<uint32_t>(local_ip) != 0;
    local_ = local_ip;
    gateway_ = gateway;
    subnet_ = subnet;
    dns_ = dns1;
    return true;
}

bool WiFiClass::disconnect(bool wifioff, bool eraseap)
{
    host().wifi_disconnect();
    if (eraseap) {
        ssid_[0] = '\0';
        password_[0] = '\0';
    }
    if (wifioff) {
        mode(apUp_ ? WIFI_AP : WIFI_OFF);
    }
    return true;
}

bool WiFiClass::reconnect()
{
    if (ssid_[0] == '\0') {
        return false;
    }
    host().wifi_begin(ssid_, password_);
    return true;
}

uint8_t WiFiClass::waitForConnectResult(unsigned long timeoutLength)
{
    unsigned long start = millis();
    while (millis() - start < timeoutLength) {
        wl_status_t s = status();
        if (s == WL_CONNECTED || s == WL_CONNECT_FAILED || s == WL_NO_SSID_AVAIL) {
            return s;
        }
        delay(100);
    }
    return status();
}

wl_status_t WiFiClass::status()
{
    if (mode_ == WIFI_OFF || mode_ == WIFI_AP) {
        return mode_ == WIFI_OFF ? WL_STOPPED : WL_DISCONNECTED;
    }
    return static_cast<wl_status_t>(host().wifi_status());
}

IPAddress WiFiClass::localIP()
{
    if (staticIp_ && status() == WL_CONNECTED) {
        return local_;
    }
    return IPAddress(host().wifi_address(0));
}

IPAddress WiFiClass::gatewayIP() { return staticIp_ && status() == WL_CONNECTED ? gateway_ : IPAddress(host().wifi_address(1)); }

IPAddress WiFiClass::subnetMask() { return staticIp_ && status() == WL_CONNECTED ? subnet_ : IPAddress(host().wifi_address(2)); }

IPAddress WiFiClass::dnsIP(uint8_t) { return staticIp_ && status() == WL_CONNECTED && static_cast<uint32_t>(dns_) != 0 ? dns_ : IPAddress(host().wifi_address(3)); }

IPAddress WiFiClass::broadcastIP()
{
    uint32_t ip = localIP();
    uint32_t mask = subnetMask();
    return IPAddress(ip | ~mask);
}

uint8_t* WiFiClass::macAddress(uint8_t* mac)
{
    static const uint8_t station[6] = {0x24, 0x0A, 0xC4, 0x00, 0x00, 0x01};
    memcpy(mac, station, 6);
    return mac;
}

String WiFiClass::macAddress() { return String("24:0A:C4:00:00:01"); }

String WiFiClass::softAPmacAddress() { return String("24:0A:C4:00:00:02"); }

int8_t WiFiClass::RSSI() { return static_cast<int8_t>(host().wifi_rssi()); }

bool WiFiClass::mode(wifi_mode_t mode)
{
    if (mode == WIFI_OFF || mode == WIFI_AP) {
        host().wifi_disconnect();
    }
    if ((mode == WIFI_OFF || mode == WIFI_STA) && apUp_) {
        softAPdisconnect();
    }
    mode_ = mode;
    return true;
}

bool WiFiClass::setHostname(const char* hostname)
{
    snprintf(hostname_, sizeof hostname_, "%s", hostname != nullptr ? hostname : "");
    return true;
}

int16_t WiFiClass::scanNetworks(bool async, bool, bool, uint32_t, uint8_t, const char*, const uint8_t*)
{
    if (mode_ == WIFI_OFF) {
        mode_ = WIFI_STA;
    }
    host().wifi_scan_start();
    if (async) {
        return WIFI_SCAN_RUNNING;
    }
    int32_t count;
    while ((count = host().wifi_scan_count()) == WIFI_SCAN_RUNNING) {
        delay(10);
    }
    return static_cast<int16_t>(count);
}

int16_t WiFiClass::scanComplete() { return static_cast<int16_t>(host().wifi_scan_count()); }

void WiFiClass::scanDelete() {}

String WiFiClass::SSID(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? String(ssid) : String();
}

int32_t WiFiClass::RSSI(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi = 0, channel, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? rssi : 0;
}

wifi_auth_mode_t WiFiClass::encryptionType(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel, open = 1;
    if (host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) != 0) {
        return WIFI_AUTH_OPEN;
    }
    return open != 0 ? WIFI_AUTH_OPEN : WIFI_AUTH_WPA2_PSK;
}

int32_t WiFiClass::channel(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel = 0, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? channel : 0;
}

String WiFiClass::BSSIDstr(uint8_t networkItem)
{
    char text[18];
    snprintf(text, sizeof text, "B0:BE:76:00:00:%02X", static_cast<unsigned>(networkItem + 1));
    return String(text);
}

bool WiFiClass::softAP(const char* ssid, const char* passphrase, int, int, int, bool)
{
    if (ssid == nullptr || ssid[0] == '\0') {
        return false;
    }
    apUp_ = host().wifi_soft_ap(ssid, passphrase != nullptr ? passphrase : "") == 1;
    if (apUp_) {
        mode_ = mode_ == WIFI_STA ? WIFI_AP_STA : WIFI_AP;
    }
    return apUp_;
}

bool WiFiClass::softAPConfig(IPAddress, IPAddress, IPAddress)
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("WiFi.softAPConfig(): the access point keeps 192.168.4.1; other addresses are not simulated.");
    }
    return true;
}

IPAddress WiFiClass::softAPIP() { return IPAddress(host().wifi_address(4)); }

bool WiFiClass::softAPdisconnect(bool wifioff)
{
    apUp_ = false;
    host().wifi_soft_ap("", "");
    if (wifioff) {
        mode_ = WIFI_OFF;
    } else if (mode_ == WIFI_AP_STA) {
        mode_ = WIFI_STA;
    }
    return true;
}

int WiFiClass::hostByName(const char* aHostname, IPAddress& aResult)
{
    if (aResult.fromString(aHostname)) {
        return 1;
    }
    int32_t lookup = host().net_resolve(aHostname);
    unsigned long start = millis();
    uint32_t ip = 0;
    int32_t state;
    while ((state = host().net_resolve_result(lookup, &ip)) == 0) {
        if (millis() - start > 10000) {
            return 0;
        }
        delay(1);
    }
    if (state != 1) {
        return 0;
    }
    aResult = IPAddress(ip);
    return 1;
}

wifi_event_id_t WiFiClass::onEvent(WiFiEventCb callback, arduino_event_id_t event)
{
    for (int i = 0; i < MaxHandlers; i++) {
        if (g_handlers[i].plain == nullptr && g_handlers[i].with_info == nullptr) {
            g_handlers[i] = Handler{callback, nullptr, event};
            startEventTask();
            return static_cast<wifi_event_id_t>(i + 1);
        }
    }
    return 0;
}

wifi_event_id_t WiFiClass::onEvent(WiFiEventFuncCb callback, arduino_event_id_t event)
{
    for (int i = 0; i < MaxHandlers; i++) {
        if (g_handlers[i].plain == nullptr && g_handlers[i].with_info == nullptr) {
            g_handlers[i] = Handler{nullptr, callback, event};
            startEventTask();
            return static_cast<wifi_event_id_t>(i + 1);
        }
    }
    return 0;
}

void WiFiClass::removeEvent(wifi_event_id_t id)
{
    if (id >= 1 && id <= MaxHandlers) {
        g_handlers[id - 1] = Handler{nullptr, nullptr, ARDUINO_EVENT_NONE};
    }
}

void WiFiClass::startEventTask()
{
    if (!g_event_task) {
        g_event_task = true;
        g_last_status = host().wifi_status();
        g_last_scan = host().wifi_scan_count();
        xTaskCreate(event_task, "arduino_events", 4096, nullptr, 19, nullptr);
    }
}

void WiFiClass::fire(arduino_event_id_t event, arduino_event_info_t info)
{
    for (const Handler& handler : g_handlers) {
        if (handler.event != ARDUINO_EVENT_MAX && handler.event != event) {
            continue;
        }
        if (handler.plain != nullptr) {
            handler.plain(event);
        } else if (handler.with_info != nullptr) {
            handler.with_info(event, info);
        }
    }
}

void WiFiClass::pollEvents()
{
    arduino_event_info_t info = {};
    int32_t now = host().wifi_status();
    if (now != g_last_status) {
        if (now == WL_CONNECTED) {
            fire(ARDUINO_EVENT_WIFI_STA_CONNECTED, info);
            info.got_ip.ip = static_cast<uint32_t>(localIP());
            fire(ARDUINO_EVENT_WIFI_STA_GOT_IP, info);
        } else if (g_last_status == WL_CONNECTED || now == WL_CONNECT_FAILED || now == WL_NO_SSID_AVAIL) {
            info.wifi_sta_disconnected.reason = now == WL_NO_SSID_AVAIL ? 201 : now == WL_CONNECT_FAILED ? 15 : 8;
            fire(ARDUINO_EVENT_WIFI_STA_DISCONNECTED, info);
        }
        g_last_status = now;
    }

    int32_t scan = host().wifi_scan_count();
    if (scan >= 0 && g_last_scan == WIFI_SCAN_RUNNING) {
        info.wifi_scan_done.number = static_cast<uint16_t>(scan);
        fire(ARDUINO_EVENT_WIFI_SCAN_DONE, info);
    }
    g_last_scan = scan;

    if (apUp_ != g_last_ap) {
        fire(apUp_ ? ARDUINO_EVENT_WIFI_AP_START : ARDUINO_EVENT_WIFI_AP_STOP, info);
        g_last_ap = apUp_;
    }
}
