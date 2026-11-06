// avr-libc's heap bounds, which the usual free-SRAM idiom reads. Firmware here is a native DLL with no
// AVR memory map: they exist so that such a sketch links, and what it computes from them means nothing.
// The build says so, on the line that names them (FirmwareBuilder.FreeSramWarnings).
int __heap_start = 0;
int* __brkval = nullptr;
