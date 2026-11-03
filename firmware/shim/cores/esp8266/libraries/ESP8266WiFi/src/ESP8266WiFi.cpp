/* ESP8266WiFi.cpp - see ESP8266WiFi.h. */
#include "ESP8266WiFi.h"

#include <stdio.h>
#include <string.h>

#include <vector>

#include <sim/host.h>

using sim::host;

ESP8266WiFiClass WiFi;

namespace {

// The host reports the ESP32 core's wl_status_t; the ESP8266 core 3.x numbers two of them differently.
constexpr int32_t HostConnectFailed = 4;
constexpr int32_t HostDisconnected = 6;

template <class Event>
struct Handler {
    std::weak_ptr<int> owner;
    std::function<void(const Event&)> callback;
};

std::vector<Handler<WiFiEventStationModeConnected>> g_connected;
std::vector<Handler<WiFiEventStationModeDisconnected>> g_disconnected;
std::vector<Handler<WiFiEventStationModeGotIP>> g_got_ip;
int32_t g_last_status = -1;

template <class Event>
WiFiEventHandler add(std::vector<Handler<Event>>& list, std::function<void(const Event&)> callback)
{
    WiFiEventHandler owner = std::make_shared<int>(0);
    list.push_back(Handler<Event>{owner, std::move(callback)});
    return owner;
}

template <class Event>
void fire(std::vector<Handler<Event>>& list, const Event& event)
{
    for (const Handler<Event>& handler : list) {
        if (!handler.owner.expired()) {
            handler.callback(event);
        }
    }
}

bool any_handlers() { return !g_connected.empty() || !g_disconnected.empty() || !g_got_ip.empty(); }

}  // namespace

// The system's turn (between loop() calls, in delay() and yield()): Wi-Fi events.
extern "C" void sim_esp8266_system(void)
{
    if (any_handlers()) {
        WiFi.pollEvents();
    }
}

wl_status_t ESP8266WiFiClass::begin(const char* ssid, const char* passphrase, int32_t, const uint8_t*, bool connect)
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

wl_status_t ESP8266WiFiClass::begin()
{
    if (ssid_[0] == '\0') {
        sim::log_warning("WiFi.begin(): no network was set before; the board has no stored credentials in the simulation.");
        return WL_CONNECT_FAILED;
    }
    host().wifi_begin(ssid_, password_);
    return status();
}

bool ESP8266WiFiClass::config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1, IPAddress)
{
    staticIp_ = static_cast<uint32_t>(local_ip) != 0;
    local_ = local_ip;
    gateway_ = gateway;
    subnet_ = subnet;
    dns_ = dns1;
    return true;
}

bool ESP8266WiFiClass::disconnect(bool wifioff)
{
    host().wifi_disconnect();
    if (wifioff) {
        mode(WIFI_OFF);
    }
    return true;
}

bool ESP8266WiFiClass::reconnect()
{
    if (ssid_[0] == '\0') {
        return false;
    }
    host().wifi_begin(ssid_, password_);
    return true;
}

int8_t ESP8266WiFiClass::waitForConnectResult(unsigned long timeoutLength)
{
    unsigned long start = millis();
    while (millis() - start < timeoutLength) {
        wl_status_t s = status();
        if (s == WL_CONNECTED || s == WL_CONNECT_FAILED || s == WL_WRONG_PASSWORD || s == WL_NO_SSID_AVAIL) {
            return static_cast<int8_t>(s);
        }
        delay(100);
    }
    return -1;
}

wl_status_t ESP8266WiFiClass::status()
{
    if (mode_ == WIFI_OFF || mode_ == WIFI_AP) {
        return WL_DISCONNECTED;
    }
    int32_t s = host().wifi_status();
    if (s == HostConnectFailed) {
        return WL_WRONG_PASSWORD;
    }
    if (s == HostDisconnected) {
        return WL_DISCONNECTED;
    }
    return static_cast<wl_status_t>(s);
}

IPAddress ESP8266WiFiClass::localIP() { return staticIp_ && status() == WL_CONNECTED ? local_ : IPAddress(host().wifi_address(0)); }

IPAddress ESP8266WiFiClass::gatewayIP() { return staticIp_ && status() == WL_CONNECTED ? gateway_ : IPAddress(host().wifi_address(1)); }

IPAddress ESP8266WiFiClass::subnetMask() { return staticIp_ && status() == WL_CONNECTED ? subnet_ : IPAddress(host().wifi_address(2)); }

IPAddress ESP8266WiFiClass::dnsIP(uint8_t)
{
    return staticIp_ && status() == WL_CONNECTED && static_cast<uint32_t>(dns_) != 0 ? dns_ : IPAddress(host().wifi_address(3));
}

IPAddress ESP8266WiFiClass::broadcastIP() { return IPAddress(static_cast<uint32_t>(localIP()) | ~static_cast<uint32_t>(subnetMask())); }

uint8_t* ESP8266WiFiClass::macAddress(uint8_t* mac)
{
    static const uint8_t station[6] = {0xC0, 0xFF, 0xEE, 0x00, 0x00, 0x01};
    memcpy(mac, station, 6);
    return mac;
}

String ESP8266WiFiClass::macAddress() { return String("C0:FF:EE:00:00:01"); }

int32_t ESP8266WiFiClass::RSSI() { return host().wifi_rssi(); }

bool ESP8266WiFiClass::hostname(const char* name)
{
    snprintf(hostname_, sizeof hostname_, "%s", name != nullptr ? name : "");
    return true;
}

bool ESP8266WiFiClass::mode(WiFiMode_t mode)
{
    if (mode == WIFI_OFF || mode == WIFI_AP) {
        host().wifi_disconnect();
    }
    if (mode == WIFI_OFF || mode == WIFI_STA) {
        host().wifi_soft_ap("", "");
    }
    mode_ = mode;
    return true;
}

int8_t ESP8266WiFiClass::scanNetworks(bool async, bool, uint8_t, uint8_t*)
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
    return static_cast<int8_t>(count);
}

int8_t ESP8266WiFiClass::scanComplete() { return static_cast<int8_t>(host().wifi_scan_count()); }

String ESP8266WiFiClass::SSID(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? String(ssid) : String();
}

int32_t ESP8266WiFiClass::RSSI(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi = 0, channel, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? rssi : 0;
}

uint8_t ESP8266WiFiClass::encryptionType(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel, open = 1;
    if (host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) != 0) {
        return 255;
    }
    return open != 0 ? ENC_TYPE_NONE : ENC_TYPE_CCMP;
}

int32_t ESP8266WiFiClass::channel(uint8_t networkItem)
{
    char ssid[33];
    int32_t rssi, channel = 0, open;
    return host().wifi_scan_entry(networkItem, ssid, sizeof ssid, &rssi, &channel, &open) == 0 ? channel : 0;
}

String ESP8266WiFiClass::BSSIDstr(uint8_t networkItem)
{
    char text[18];
    snprintf(text, sizeof text, "B0:BE:76:00:00:%02X", static_cast<unsigned>(networkItem + 1));
    return String(text);
}

bool ESP8266WiFiClass::softAP(const char* ssid, const char* passphrase, int, int, int)
{
    if (ssid == nullptr || ssid[0] == '\0') {
        return false;
    }
    bool up = host().wifi_soft_ap(ssid, passphrase != nullptr ? passphrase : "") == 1;
    if (up) {
        mode_ = mode_ == WIFI_STA ? WIFI_AP_STA : WIFI_AP;
    }
    return up;
}

bool ESP8266WiFiClass::softAPConfig(IPAddress, IPAddress, IPAddress)
{
    static bool warned = false;
    if (!warned) {
        warned = true;
        sim::log_warning("WiFi.softAPConfig(): the access point keeps 192.168.4.1; other addresses are not simulated.");
    }
    return true;
}

IPAddress ESP8266WiFiClass::softAPIP() { return IPAddress(host().wifi_address(4)); }

bool ESP8266WiFiClass::softAPdisconnect(bool wifioff)
{
    host().wifi_soft_ap("", "");
    mode_ = wifioff ? WIFI_OFF : (mode_ == WIFI_AP_STA ? WIFI_STA : WIFI_OFF);
    return true;
}

int ESP8266WiFiClass::hostByName(const char* aHostname, IPAddress& aResult) { return hostByName(aHostname, aResult, 10000); }

int ESP8266WiFiClass::hostByName(const char* aHostname, IPAddress& aResult, uint32_t timeout_ms)
{
    if (aResult.fromString(aHostname)) {
        return 1;
    }
    int32_t lookup = host().net_resolve(aHostname);
    unsigned long start = millis();
    uint32_t ip = 0;
    int32_t state;
    while ((state = host().net_resolve_result(lookup, &ip)) == 0) {
        if (millis() - start > timeout_ms) {
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

WiFiEventHandler ESP8266WiFiClass::onStationModeConnected(std::function<void(const WiFiEventStationModeConnected&)> handler)
{
    g_last_status = host().wifi_status();
    return add(g_connected, std::move(handler));
}

WiFiEventHandler ESP8266WiFiClass::onStationModeDisconnected(std::function<void(const WiFiEventStationModeDisconnected&)> handler)
{
    g_last_status = host().wifi_status();
    return add(g_disconnected, std::move(handler));
}

WiFiEventHandler ESP8266WiFiClass::onStationModeGotIP(std::function<void(const WiFiEventStationModeGotIP&)> handler)
{
    g_last_status = host().wifi_status();
    return add(g_got_ip, std::move(handler));
}

void ESP8266WiFiClass::pollEvents()
{
    int32_t now = host().wifi_status();
    if (now == g_last_status) {
        return;
    }

    int32_t before = g_last_status;
    g_last_status = now;
    if (now == WL_CONNECTED) {
        WiFiEventStationModeConnected connected{String(ssid_), {0xB0, 0xBE, 0x76, 0x00, 0x00, 0x01}, 6};
        fire(g_connected, connected);
        WiFiEventStationModeGotIP got{localIP(), subnetMask(), gatewayIP()};
        fire(g_got_ip, got);
    } else if (before == WL_CONNECTED || now == HostConnectFailed || now == WL_NO_SSID_AVAIL) {
        uint8_t reason = now == WL_NO_SSID_AVAIL ? 201 : now == HostConnectFailed ? 202 : 8;
        WiFiEventStationModeDisconnected lost{String(ssid_), {0xB0, 0xBE, 0x76, 0x00, 0x00, 0x01}, reason};
        fire(g_disconnected, lost);
    }
}
