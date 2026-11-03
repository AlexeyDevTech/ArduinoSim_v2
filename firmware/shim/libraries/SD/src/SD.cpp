// The Arduino SD API on SdFat (see SD.h). File copies share one open SdFat file, reference counted,
// so passing a File by value (as Arduino sketches do) keeps it open until the last copy closes.
#include "SD.h"

namespace SDLib {

SDClass SD;

struct File::Shared {
    File32 file;
    int references;
    char name[64];
};

File::File() : _shared(nullptr) {}

File::File(Shared* shared) : _shared(shared) {}

File::File(const File& other) : Stream(), _shared(other._shared)
{
    if (_shared != nullptr) {
        _shared->references++;
    }
}

File& File::operator=(const File& other)
{
    if (this != &other) {
        release();
        _shared = other._shared;
        if (_shared != nullptr) {
            _shared->references++;
        }
    }
    return *this;
}

File::~File() { release(); }

void File::release()
{
    if (_shared != nullptr && --_shared->references == 0) {
        _shared->file.close();
        delete _shared;
    }
    _shared = nullptr;
}

size_t File::write(uint8_t value) { return write(&value, 1); }

size_t File::write(const uint8_t* buffer, size_t size)
{
    if (_shared == nullptr) {
        setWriteError();
        return 0;
    }
    size_t written = _shared->file.write(buffer, size);
    if (written != size) {
        setWriteError();
    }
    return written;
}

int File::availableForWrite() { return _shared != nullptr ? _shared->file.availableForWrite() : 0; }

int File::read() { return _shared != nullptr ? _shared->file.read() : -1; }

int File::peek() { return _shared != nullptr ? _shared->file.peek() : -1; }

int File::available()
{
    if (_shared == nullptr) {
        return 0;
    }
    uint32_t left = _shared->file.fileSize() - _shared->file.curPosition();
    return left > 0x7FFF ? 0x7FFF : static_cast<int>(left);
}

void File::flush()
{
    if (_shared != nullptr) {
        _shared->file.sync();
    }
}

int File::read(void* buffer, uint16_t count) { return _shared != nullptr ? _shared->file.read(buffer, count) : -1; }

bool File::seek(uint32_t position) { return _shared != nullptr && _shared->file.seekSet(position); }

uint32_t File::position() { return _shared != nullptr ? _shared->file.curPosition() : 0; }

uint32_t File::size() { return _shared != nullptr ? _shared->file.fileSize() : 0; }

void File::close()
{
    if (_shared != nullptr) {
        _shared->file.close();
    }
    release();
}

File::operator bool() { return _shared != nullptr && _shared->file.isOpen(); }

char* File::name()
{
    static char empty[1] = {0};
    return _shared != nullptr ? _shared->name : empty;
}

bool File::isDirectory() { return _shared != nullptr && _shared->file.isDir(); }

File File::openNextFile(uint8_t mode)
{
    if (_shared == nullptr) {
        return File();
    }
    auto* next = new Shared{File32(), 1, {0}};
    if (!next->file.openNext(&_shared->file, mode)) {
        delete next;
        return File();
    }
    next->file.getName(next->name, sizeof next->name);
    return File(next);
}

void File::rewindDirectory()
{
    if (_shared != nullptr && _shared->file.isDir()) {
        _shared->file.rewind();
    }
}

bool SDClass::begin(uint8_t csPin) { return begin(SD_SCK_MHZ(4), csPin); }

bool SDClass::begin(uint32_t clock, uint8_t csPin)
{
    _begun = _fs.begin(SdSpiConfig(csPin, SHARED_SPI, clock));
    return _begun;
}

void SDClass::end()
{
    _fs.end();
    _begun = false;
}

File SDClass::open(const char* path, uint8_t mode)
{
    if (!_begun) {
        return File();
    }
    auto* opened = new File::Shared{File32(), 1, {0}};
    if (!opened->file.open(&_fs, path, mode)) {
        delete opened;
        return File();
    }
    if ((mode & O_AT_END) != 0) {
        opened->file.seekEnd();
    }
    opened->file.getName(opened->name, sizeof opened->name);
    return File(opened);
}

bool SDClass::exists(const char* path) { return _begun && _fs.exists(path); }

bool SDClass::mkdir(const char* path) { return _begun && _fs.mkdir(path, true); }

bool SDClass::remove(const char* path) { return _begun && _fs.remove(path); }

bool SDClass::rmdir(const char* path) { return _begun && _fs.rmdir(path); }

}  // namespace SDLib