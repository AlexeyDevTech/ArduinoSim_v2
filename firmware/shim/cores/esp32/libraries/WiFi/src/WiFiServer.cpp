/* WiFiServer.cpp - see WiFiServer.h. */
#include "WiFiServer.h"

#include <sim/host.h>

using sim::host;

void WiFiServer::begin(uint16_t port, int)
{
    end();
    port_ = port;
    int32_t handle = host().net_listen(port);
    listener_ = handle > 0 ? handle : 0;
}

bool WiFiServer::hasClient()
{
    if (listener_ == 0) {
        return false;
    }
    if (waiting_ == 0) {
        waiting_ = host().net_accept(listener_);
    }
    if (waiting_ == 0) {
        sim_task_switch_point();
    }
    return waiting_ != 0;
}

WiFiClient WiFiServer::accept()
{
    if (!hasClient()) {
        return WiFiClient();
    }
    int32_t handle = waiting_;
    waiting_ = 0;
    return WiFiClient(handle);
}

void WiFiServer::end()
{
    if (listener_ != 0) {
        host().net_close(listener_);
        listener_ = 0;
    }
}