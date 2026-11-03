/*
 * HTTPClient.h - the Arduino-ESP32 HTTPClient API, written for ArduSim over WiFiClient (ADR-010):
 * HTTP/1.1, one request per connection ("Connection: close"), bodies by Content-Length, chunked
 * or until the server closes. https:// URLs use WiFiClientSecure with the Windows certificate
 * store unless a client with its own CA is passed to begin().
 */
#ifndef HTTPClient_H_
#define HTTPClient_H_

#include <Arduino.h>
#include <WiFiClient.h>
#include <WiFiClientSecure.h>

#define HTTPCLIENT_DEFAULT_TCP_TIMEOUT (5000)

#define HTTPC_ERROR_CONNECTION_REFUSED (-1)
#define HTTPC_ERROR_SEND_HEADER_FAILED (-2)
#define HTTPC_ERROR_SEND_PAYLOAD_FAILED (-3)
#define HTTPC_ERROR_NOT_CONNECTED (-4)
#define HTTPC_ERROR_CONNECTION_LOST (-5)
#define HTTPC_ERROR_NO_STREAM (-6)
#define HTTPC_ERROR_NO_HTTP_SERVER (-7)
#define HTTPC_ERROR_TOO_LESS_RAM (-8)
#define HTTPC_ERROR_ENCODING (-9)
#define HTTPC_ERROR_STREAM_WRITE (-10)
#define HTTPC_ERROR_READ_TIMEOUT (-11)

typedef enum {
    HTTP_CODE_CONTINUE = 100,
    HTTP_CODE_OK = 200,
    HTTP_CODE_CREATED = 201,
    HTTP_CODE_ACCEPTED = 202,
    HTTP_CODE_NO_CONTENT = 204,
    HTTP_CODE_MOVED_PERMANENTLY = 301,
    HTTP_CODE_FOUND = 302,
    HTTP_CODE_SEE_OTHER = 303,
    HTTP_CODE_NOT_MODIFIED = 304,
    HTTP_CODE_TEMPORARY_REDIRECT = 307,
    HTTP_CODE_PERMANENT_REDIRECT = 308,
    HTTP_CODE_BAD_REQUEST = 400,
    HTTP_CODE_UNAUTHORIZED = 401,
    HTTP_CODE_FORBIDDEN = 403,
    HTTP_CODE_NOT_FOUND = 404,
    HTTP_CODE_METHOD_NOT_ALLOWED = 405,
    HTTP_CODE_TOO_MANY_REQUESTS = 429,
    HTTP_CODE_INTERNAL_SERVER_ERROR = 500,
    HTTP_CODE_BAD_GATEWAY = 502,
    HTTP_CODE_SERVICE_UNAVAILABLE = 503,
    HTTP_CODE_GATEWAY_TIMEOUT = 504
} t_http_codes;

typedef enum { HTTPC_DISABLE_FOLLOW_REDIRECTS, HTTPC_STRICT_FOLLOW_REDIRECTS, HTTPC_FORCE_FOLLOW_REDIRECTS } followRedirects_t;

class HTTPClient {
public:
    HTTPClient() = default;
    ~HTTPClient();

    bool begin(String url);
    bool begin(WiFiClient& client, String url);
    bool begin(String host, uint16_t port, String uri = "/", bool https = false);
    bool begin(WiFiClient& client, String host, uint16_t port, String uri = "/", bool https = false);
    void end();
    bool connected();

    void setReuse(bool) {}
    void setUserAgent(const String& userAgent) { userAgent_ = userAgent; }
    void setAuthorization(const char* user, const char* password);
    void setAuthorization(const char* auth);
    void setConnectTimeout(int32_t connectTimeout) { connectTimeout_ = connectTimeout; }
    void setTimeout(uint16_t timeout) { timeout_ = timeout; }
    void setFollowRedirects(followRedirects_t follow) { follow_ = follow; }
    void setRedirectLimit(uint16_t limit) { redirectLimit_ = limit; }
    void useHTTP10(bool) {}

    void addHeader(const String& name, const String& value, bool first = false, bool replace = true);
    void collectHeaders(const char* headerKeys[], const size_t headerKeysCount);
    String header(const char* name);
    String header(size_t i);
    String headerName(size_t i);
    int headers() { return static_cast<int>(collectedCount_); }
    bool hasHeader(const char* name);

    int GET();
    int PATCH(uint8_t* payload, size_t size) { return sendRequest("PATCH", payload, size); }
    int PATCH(const String& payload) { return sendRequest("PATCH", payload); }
    int POST(uint8_t* payload, size_t size) { return sendRequest("POST", payload, size); }
    int POST(const String& payload) { return sendRequest("POST", payload); }
    int PUT(uint8_t* payload, size_t size) { return sendRequest("PUT", payload, size); }
    int PUT(const String& payload) { return sendRequest("PUT", payload); }
    int sendRequest(const char* type, const String& payload);
    int sendRequest(const char* type, uint8_t* payload = nullptr, size_t size = 0);

    int getSize() { return size_; }
    const String& getLocation() { return location_; }
    WiFiClient& getStream();
    WiFiClient* getStreamPtr();
    int writeToStream(Stream* stream);
    String getString();
    static String errorToString(int error);

private:
    struct Header {
        String name;
        String value;
    };

    bool parse(const String& url);
    bool connect();
    int handleHeaderResponse();
    bool readLine(String& line);
    void disconnect();

    WiFiClient* client_ = nullptr;
    WiFiClient* own_ = nullptr;
    bool secure_ = false;
    String host_;
    uint16_t port_ = 80;
    String uri_ = "/";
    String headers_;
    String userAgent_ = "ESP32HTTPClient";
    String base64Auth_;
    String location_;
    int32_t connectTimeout_ = HTTPCLIENT_DEFAULT_TCP_TIMEOUT;
    uint16_t timeout_ = HTTPCLIENT_DEFAULT_TCP_TIMEOUT;
    followRedirects_t follow_ = HTTPC_DISABLE_FOLLOW_REDIRECTS;
    uint16_t redirectLimit_ = 10;
    int code_ = 0;
    int size_ = -1;
    bool chunked_ = false;
    Header* collected_ = nullptr;
    size_t collectedCount_ = 0;
};

#endif