/* HTTPClient.cpp - see HTTPClient.h. */
#include "HTTPClient.h"

#include <stdio.h>
#include <string.h>

namespace {

String base64(const String& text)
{
    static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    String out;
    const char* s = text.c_str();
    size_t n = text.length();
    for (size_t i = 0; i < n; i += 3) {
        uint32_t v = static_cast<uint32_t>(static_cast<uint8_t>(s[i])) << 16;
        if (i + 1 < n) {
            v |= static_cast<uint32_t>(static_cast<uint8_t>(s[i + 1])) << 8;
        }
        if (i + 2 < n) {
            v |= static_cast<uint8_t>(s[i + 2]);
        }
        out += alphabet[(v >> 18) & 63];
        out += alphabet[(v >> 12) & 63];
        out += i + 1 < n ? alphabet[(v >> 6) & 63] : '=';
        out += i + 2 < n ? alphabet[v & 63] : '=';
    }
    return out;
}

}  // namespace

HTTPClient::~HTTPClient()
{
    end();
    delete[] collected_;
}

bool HTTPClient::parse(const String& url)
{
    String rest = url;
    int scheme = rest.indexOf("://");
    if (scheme < 0) {
        return false;
    }
    String protocol = rest.substring(0, static_cast<unsigned>(scheme));
    rest.remove(0, static_cast<unsigned>(scheme) + 3);
    if (protocol == "https") {
        secure_ = true;
        port_ = 443;
    } else if (protocol == "http") {
        secure_ = false;
        port_ = 80;
    } else {
        return false;
    }

    int slash = rest.indexOf('/');
    String authority = slash >= 0 ? rest.substring(0, static_cast<unsigned>(slash)) : rest;
    uri_ = slash >= 0 ? rest.substring(static_cast<unsigned>(slash)) : String("/");
    int at = authority.indexOf('@');
    if (at >= 0) {
        base64Auth_ = base64(authority.substring(0, static_cast<unsigned>(at)));
        authority.remove(0, static_cast<unsigned>(at) + 1);
    }
    int colon = authority.indexOf(':');
    if (colon >= 0) {
        port_ = static_cast<uint16_t>(authority.substring(static_cast<unsigned>(colon) + 1).toInt());
        host_ = authority.substring(0, static_cast<unsigned>(colon));
    } else {
        host_ = authority;
    }
    return host_.length() > 0;
}

bool HTTPClient::begin(String url)
{
    end();
    if (!parse(url)) {
        return false;
    }
    own_ = secure_ ? new WiFiClientSecure() : new WiFiClient();
    client_ = own_;
    return true;
}

bool HTTPClient::begin(WiFiClient& client, String url)
{
    end();
    if (!parse(url)) {
        return false;
    }
    client_ = &client;
    return true;
}

bool HTTPClient::begin(String host, uint16_t port, String uri, bool https)
{
    return begin(String(https ? "https://" : "http://") + host + ":" + String(port) + uri);
}

bool HTTPClient::begin(WiFiClient& client, String host, uint16_t port, String uri, bool https)
{
    return begin(client, String(https ? "https://" : "http://") + host + ":" + String(port) + uri);
}

void HTTPClient::disconnect()
{
    if (client_ != nullptr) {
        client_->stop();
    }
}

void HTTPClient::end()
{
    disconnect();
    delete own_;
    own_ = nullptr;
    client_ = nullptr;
    headers_ = "";
    code_ = 0;
    size_ = -1;
    chunked_ = false;
}

bool HTTPClient::connected() { return client_ != nullptr && client_->connected(); }

void HTTPClient::setAuthorization(const char* user, const char* password)
{
    base64Auth_ = base64(String(user) + ":" + String(password));
}

void HTTPClient::setAuthorization(const char* auth) { base64Auth_ = auth; }

void HTTPClient::addHeader(const String& name, const String& value, bool first, bool)
{
    String line = name + ": " + value + "\r\n";
    headers_ = first ? line + headers_ : headers_ + line;
}

void HTTPClient::collectHeaders(const char* headerKeys[], const size_t headerKeysCount)
{
    delete[] collected_;
    collected_ = new Header[headerKeysCount];
    collectedCount_ = headerKeysCount;
    for (size_t i = 0; i < headerKeysCount; i++) {
        collected_[i].name = headerKeys[i];
    }
}

String HTTPClient::header(const char* name)
{
    for (size_t i = 0; i < collectedCount_; i++) {
        if (collected_[i].name.equalsIgnoreCase(name)) {
            return collected_[i].value;
        }
    }
    return String();
}

String HTTPClient::header(size_t i) { return i < collectedCount_ ? collected_[i].value : String(); }

String HTTPClient::headerName(size_t i) { return i < collectedCount_ ? collected_[i].name : String(); }

bool HTTPClient::hasHeader(const char* name) { return header(name).length() > 0; }

bool HTTPClient::connect()
{
    if (client_ == nullptr) {
        return false;
    }
    if (client_->connected()) {
        return true;
    }
    client_->setTimeout(static_cast<uint32_t>((timeout_ + 999) / 1000));
    return client_->connect(host_.c_str(), port_) == 1;
}

int HTTPClient::GET() { return sendRequest("GET"); }

int HTTPClient::sendRequest(const char* type, const String& payload)
{
    return sendRequest(type, reinterpret_cast<uint8_t*>(const_cast<char*>(payload.c_str())), payload.length());
}

int HTTPClient::sendRequest(const char* type, uint8_t* payload, size_t size)
{
    for (uint16_t redirects = 0;; redirects++) {
        if (!connect()) {
            return HTTPC_ERROR_CONNECTION_REFUSED;
        }

        String request = String(type) + " " + uri_ + " HTTP/1.1\r\nHost: " + host_;
        if ((secure_ && port_ != 443) || (!secure_ && port_ != 80)) {
            request += ":" + String(port_);
        }
        request += "\r\nUser-Agent: " + userAgent_ + "\r\nConnection: close\r\nAccept-Encoding: identity;q=1,chunked;q=0.1,*;q=0\r\n";
        if (base64Auth_.length() > 0) {
            request += "Authorization: Basic " + base64Auth_ + "\r\n";
        }
        if (payload != nullptr && size > 0) {
            request += "Content-Length: " + String(static_cast<unsigned long>(size)) + "\r\n";
        }
        request += headers_ + "\r\n";
        if (client_->write(reinterpret_cast<const uint8_t*>(request.c_str()), request.length()) != request.length()) {
            return HTTPC_ERROR_SEND_HEADER_FAILED;
        }
        if (payload != nullptr && size > 0 && client_->write(payload, size) != size) {
            return HTTPC_ERROR_SEND_PAYLOAD_FAILED;
        }

        int code = handleHeaderResponse();
        bool redirect = code == HTTP_CODE_MOVED_PERMANENTLY || code == HTTP_CODE_FOUND || code == HTTP_CODE_SEE_OTHER ||
                        code == HTTP_CODE_TEMPORARY_REDIRECT || code == HTTP_CODE_PERMANENT_REDIRECT;
        bool follow = follow_ == HTTPC_FORCE_FOLLOW_REDIRECTS ||
                      (follow_ == HTTPC_STRICT_FOLLOW_REDIRECTS && (strcmp(type, "GET") == 0 || strcmp(type, "HEAD") == 0));
        if (!redirect || !follow || location_.length() == 0 || redirects >= redirectLimit_) {
            return code;
        }

        disconnect();
        if (location_.startsWith("/")) {
            uri_ = location_;
        } else {
            WiFiClient* previous = own_;
            if (!parse(location_)) {
                return code;
            }
            if (previous != nullptr) {
                delete own_;
                own_ = secure_ ? new WiFiClientSecure() : new WiFiClient();
                client_ = own_;
            }
        }
        if (code == HTTP_CODE_SEE_OTHER) {
            type = "GET";
            payload = nullptr;
            size = 0;
        }
    }
}

bool HTTPClient::readLine(String& line)
{
    line = "";
    unsigned long start = millis();
    for (;;) {
        int c = client_->read();
        if (c < 0) {
            if (!client_->connected()) {
                return line.length() > 0;
            }
            if (millis() - start > timeout_) {
                return false;
            }
            delay(1);
            continue;
        }
        if (c == '\n') {
            if (line.endsWith("\r")) {
                line.remove(line.length() - 1);
            }
            return true;
        }
        line += static_cast<char>(c);
    }
}

int HTTPClient::handleHeaderResponse()
{
    code_ = 0;
    size_ = -1;
    chunked_ = false;
    location_ = "";
    for (size_t i = 0; i < collectedCount_; i++) {
        collected_[i].value = "";
    }

    String line;
    if (!readLine(line)) {
        return HTTPC_ERROR_READ_TIMEOUT;
    }
    if (!line.startsWith("HTTP/")) {
        return HTTPC_ERROR_NO_HTTP_SERVER;
    }
    int space = line.indexOf(' ');
    code_ = line.substring(static_cast<unsigned>(space) + 1, static_cast<unsigned>(space) + 4).toInt();

    while (readLine(line) && line.length() > 0) {
        int colon = line.indexOf(':');
        if (colon < 0) {
            continue;
        }
        String name = line.substring(0, static_cast<unsigned>(colon));
        String value = line.substring(static_cast<unsigned>(colon) + 1);
        value.trim();
        if (name.equalsIgnoreCase("Content-Length")) {
            size_ = value.toInt();
        } else if (name.equalsIgnoreCase("Transfer-Encoding") && value.equalsIgnoreCase("chunked")) {
            chunked_ = true;
        } else if (name.equalsIgnoreCase("Location")) {
            location_ = value;
        }
        for (size_t i = 0; i < collectedCount_; i++) {
            if (collected_[i].name.equalsIgnoreCase(name)) {
                collected_[i].value = value;
            }
        }
    }
    return code_;
}

WiFiClient& HTTPClient::getStream()
{
    static WiFiClient none;
    return client_ != nullptr ? *client_ : none;
}

WiFiClient* HTTPClient::getStreamPtr() { return client_; }

int HTTPClient::writeToStream(Stream* stream)
{
    String body = getString();
    return stream != nullptr ? static_cast<int>(stream->print(body)) : HTTPC_ERROR_NO_STREAM;
}

String HTTPClient::getString()
{
    String body;
    if (client_ == nullptr) {
        return body;
    }

    unsigned long last = millis();
    auto read_byte = [this, &last]() -> int {
        for (;;) {
            int c = client_->read();
            if (c >= 0) {
                last = millis();
                return c;
            }
            if (!client_->connected() || millis() - last > timeout_) {
                return -1;
            }
            delay(1);
        }
    };

    if (chunked_) {
        String line;
        while (readLine(line)) {
            long chunk = strtol(line.c_str(), nullptr, 16);
            if (chunk <= 0) {
                break;
            }
            for (long i = 0; i < chunk; i++) {
                int c = read_byte();
                if (c < 0) {
                    return body;
                }
                body += static_cast<char>(c);
            }
            readLine(line);
        }
        return body;
    }

    for (int count = 0; size_ < 0 || count < size_; count++) {
        int c = read_byte();
        if (c < 0) {
            break;
        }
        body += static_cast<char>(c);
    }
    return body;
}

String HTTPClient::errorToString(int error)
{
    switch (error) {
    case HTTPC_ERROR_CONNECTION_REFUSED: return F("connection refused");
    case HTTPC_ERROR_SEND_HEADER_FAILED: return F("send header failed");
    case HTTPC_ERROR_SEND_PAYLOAD_FAILED: return F("send payload failed");
    case HTTPC_ERROR_NOT_CONNECTED: return F("not connected");
    case HTTPC_ERROR_CONNECTION_LOST: return F("connection lost");
    case HTTPC_ERROR_NO_STREAM: return F("no stream");
    case HTTPC_ERROR_NO_HTTP_SERVER: return F("no HTTP server");
    case HTTPC_ERROR_TOO_LESS_RAM: return F("too less ram");
    case HTTPC_ERROR_ENCODING: return F("Transfer-Encoding not supported");
    case HTTPC_ERROR_STREAM_WRITE: return F("Stream write error");
    case HTTPC_ERROR_READ_TIMEOUT: return F("read Timeout");
    default: return String();
    }
}
