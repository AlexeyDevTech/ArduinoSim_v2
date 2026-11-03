/*
 * SD.h - the Arduino SD library's API for ArduSim, on SdFat (docs/adr/ADR-012-outputs.md).
 *
 * The Arduino SD library is GPL-3.0; this is a separate implementation of its API (MIT, as the
 * rest of the shim) on the vendored SdFat. FILE_WRITE opens for reading and writing, creates the
 * file and appends, as in the Arduino library. Names may be long (SdFat's LFN support).
 */
#ifndef SD_h
#define SD_h

#include <Arduino.h>

// SdFat leaves the name File alone when it believes FS.h is there; here File is the SD library's.
#ifndef HAS_INCLUDE_FS_H
#define HAS_INCLUDE_FS_H
#endif
#ifndef DISABLE_FS_H_WARNING
#define DISABLE_FS_H_WARNING
#endif
#include <SdFat.h>

#define FILE_READ O_RDONLY
#define FILE_WRITE (O_RDWR | O_CREAT | O_AT_END)

#ifndef SD_CHIP_SELECT_PIN
#define SD_CHIP_SELECT_PIN SS
#endif

namespace SDLib {

class File : public Stream {
public:
    File();
    File(const File& other);
    File& operator=(const File& other);
    ~File();

    size_t write(uint8_t value) override;
    size_t write(const uint8_t* buffer, size_t size) override;
    int availableForWrite() override;
    int read() override;
    int peek() override;
    int available() override;
    void flush() override;
    int read(void* buffer, uint16_t count);
    bool seek(uint32_t position);
    uint32_t position();
    uint32_t size();
    void close();
    operator bool();
    char* name();
    bool isDirectory();
    File openNextFile(uint8_t mode = O_RDONLY);
    void rewindDirectory();

    using Print::write;

private:
    friend class SDClass;

    struct Shared;
    explicit File(Shared* shared);
    void release();

    Shared* _shared;
};

class SDClass {
public:
    bool begin(uint8_t csPin = SD_CHIP_SELECT_PIN);
    bool begin(uint32_t clock, uint8_t csPin);
    void end();

    File open(const char* path, uint8_t mode = FILE_READ);
    File open(const String& path, uint8_t mode = FILE_READ) { return open(path.c_str(), mode); }

    bool exists(const char* path);
    bool exists(const String& path) { return exists(path.c_str()); }
    bool mkdir(const char* path);
    bool mkdir(const String& path) { return mkdir(path.c_str()); }
    bool remove(const char* path);
    bool remove(const String& path) { return remove(path.c_str()); }
    bool rmdir(const char* path);
    bool rmdir(const String& path) { return rmdir(path.c_str()); }

private:
    SdFat32 _fs;
    bool _begun = false;
};

extern SDClass SD;

}  // namespace SDLib

using namespace SDLib;
typedef SDLib::File SDFile;
typedef SDLib::SDClass SDFileSystemClass;
#define SDFileSystem SDLib::SD

#endif