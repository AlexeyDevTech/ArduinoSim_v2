/*
 * sim/registers.h - register proxy objects behind avr/io.h (C++ only).
 */
#ifndef SIM_REGISTERS_H
#define SIM_REGISTERS_H

#include <stdint.h>

namespace sim {

enum PortId : uint8_t { PortB = 0, PortC = 1, PortD = 2 };
enum PortRegisterKind : uint8_t { PortOutput, PortDirection, PortInput };

uint8_t port_register_read(uint8_t port, uint8_t kind);
void port_register_write(uint8_t port, uint8_t kind, uint8_t value);
void unsimulated_register_written(uint8_t id, const char* name);
uint8_t status_register_read();
void status_register_write(uint8_t value);

/* DDRx, PORTx, PINx. */
class PortRegister {
public:
    constexpr PortRegister(uint8_t port, uint8_t kind) : port_(port), kind_(kind) {}
    operator uint8_t() const { return port_register_read(port_, kind_); }
    const PortRegister& operator=(uint8_t value) const
    {
        port_register_write(port_, kind_, value);
        return *this;
    }
    const PortRegister& operator|=(uint8_t value) const { return *this = static_cast<uint8_t>(*this | value); }
    const PortRegister& operator&=(uint8_t value) const { return *this = static_cast<uint8_t>(*this & value); }
    const PortRegister& operator^=(uint8_t value) const { return *this = static_cast<uint8_t>(*this ^ value); }

private:
    uint8_t port_;
    uint8_t kind_;
};

/* A register the simulator does not model: writes warn once, reads return 0. */
template <typename T>
class UnsimulatedRegister {
public:
    constexpr UnsimulatedRegister(uint8_t id, const char* name) : id_(id), name_(name) {}
    operator T() const { return 0; }
    const UnsimulatedRegister& operator=(T) const
    {
        unsimulated_register_written(id_, name_);
        return *this;
    }
    const UnsimulatedRegister& operator|=(T value) const { return *this = value; }
    const UnsimulatedRegister& operator&=(T value) const { return *this = value; }
    const UnsimulatedRegister& operator^=(T value) const { return *this = value; }

private:
    uint8_t id_;
    const char* name_;
};

/* SREG: only the I bit (7) is modelled. */
class StatusRegister {
public:
    constexpr StatusRegister() {}
    operator uint8_t() const { return status_register_read(); }
    const StatusRegister& operator=(uint8_t value) const
    {
        status_register_write(value);
        return *this;
    }
    const StatusRegister& operator|=(uint8_t value) const { return *this = static_cast<uint8_t>(*this | value); }
    const StatusRegister& operator&=(uint8_t value) const { return *this = static_cast<uint8_t>(*this & value); }
};

namespace reg {
inline constexpr PortRegister PORTB_{PortB, PortOutput};
inline constexpr PortRegister DDRB_{PortB, PortDirection};
inline constexpr PortRegister PINB_{PortB, PortInput};
inline constexpr PortRegister PORTC_{PortC, PortOutput};
inline constexpr PortRegister DDRC_{PortC, PortDirection};
inline constexpr PortRegister PINC_{PortC, PortInput};
inline constexpr PortRegister PORTD_{PortD, PortOutput};
inline constexpr PortRegister DDRD_{PortD, PortDirection};
inline constexpr PortRegister PIND_{PortD, PortInput};
inline constexpr StatusRegister SREG_{};

#define SIM_UNSIMULATED_8(id, name) inline constexpr UnsimulatedRegister<uint8_t> name##_{id, #name};
#define SIM_UNSIMULATED_16(id, name) inline constexpr UnsimulatedRegister<uint16_t> name##_{id, #name};
SIM_UNSIMULATED_8(0, TCCR0A)
SIM_UNSIMULATED_8(1, TCCR0B)
SIM_UNSIMULATED_8(2, TCNT0)
SIM_UNSIMULATED_8(3, OCR0A)
SIM_UNSIMULATED_8(4, OCR0B)
SIM_UNSIMULATED_8(5, TIMSK0)
SIM_UNSIMULATED_8(6, TIFR0)
SIM_UNSIMULATED_8(7, TCCR1A)
SIM_UNSIMULATED_8(8, TCCR1B)
SIM_UNSIMULATED_8(9, TCCR1C)
SIM_UNSIMULATED_16(10, TCNT1)
SIM_UNSIMULATED_8(11, TCNT1L)
SIM_UNSIMULATED_8(12, TCNT1H)
SIM_UNSIMULATED_16(13, OCR1A)
SIM_UNSIMULATED_8(14, OCR1AL)
SIM_UNSIMULATED_8(15, OCR1AH)
SIM_UNSIMULATED_16(16, OCR1B)
SIM_UNSIMULATED_8(17, OCR1BL)
SIM_UNSIMULATED_8(18, OCR1BH)
SIM_UNSIMULATED_16(19, ICR1)
SIM_UNSIMULATED_8(20, ICR1L)
SIM_UNSIMULATED_8(21, ICR1H)
SIM_UNSIMULATED_8(22, TIMSK1)
SIM_UNSIMULATED_8(23, TIFR1)
SIM_UNSIMULATED_8(24, TCCR2A)
SIM_UNSIMULATED_8(25, TCCR2B)
SIM_UNSIMULATED_8(26, TCNT2)
SIM_UNSIMULATED_8(27, OCR2A)
SIM_UNSIMULATED_8(28, OCR2B)
SIM_UNSIMULATED_8(29, TIMSK2)
SIM_UNSIMULATED_8(30, TIFR2)
SIM_UNSIMULATED_8(31, ASSR)
SIM_UNSIMULATED_8(32, GTCCR)
SIM_UNSIMULATED_8(33, ADMUX)
SIM_UNSIMULATED_8(34, ADCSRA)
SIM_UNSIMULATED_8(35, ADCSRB)
SIM_UNSIMULATED_16(36, ADC)
SIM_UNSIMULATED_16(37, ADCW)
SIM_UNSIMULATED_8(38, ADCL)
SIM_UNSIMULATED_8(39, ADCH)
SIM_UNSIMULATED_8(40, DIDR0)
SIM_UNSIMULATED_8(41, DIDR1)
SIM_UNSIMULATED_8(42, ACSR)
SIM_UNSIMULATED_8(43, UCSR0A)
SIM_UNSIMULATED_8(44, UCSR0B)
SIM_UNSIMULATED_8(45, UCSR0C)
SIM_UNSIMULATED_16(46, UBRR0)
SIM_UNSIMULATED_8(47, UBRR0L)
SIM_UNSIMULATED_8(48, UBRR0H)
SIM_UNSIMULATED_8(49, UDR0)
SIM_UNSIMULATED_8(50, SPCR)
SIM_UNSIMULATED_8(51, SPSR)
SIM_UNSIMULATED_8(52, SPDR)
SIM_UNSIMULATED_8(53, TWBR)
SIM_UNSIMULATED_8(54, TWSR)
SIM_UNSIMULATED_8(55, TWAR)
SIM_UNSIMULATED_8(56, TWDR)
SIM_UNSIMULATED_8(57, TWCR)
SIM_UNSIMULATED_8(58, TWAMR)
SIM_UNSIMULATED_8(59, EICRA)
SIM_UNSIMULATED_8(60, EIMSK)
SIM_UNSIMULATED_8(61, EIFR)
SIM_UNSIMULATED_8(62, PCICR)
SIM_UNSIMULATED_8(63, PCIFR)
SIM_UNSIMULATED_8(64, PCMSK0)
SIM_UNSIMULATED_8(65, PCMSK1)
SIM_UNSIMULATED_8(66, PCMSK2)
SIM_UNSIMULATED_8(67, WDTCSR)
SIM_UNSIMULATED_8(68, MCUSR)
SIM_UNSIMULATED_8(69, MCUCR)
SIM_UNSIMULATED_8(70, SMCR)
SIM_UNSIMULATED_8(71, PRR)
SIM_UNSIMULATED_8(72, OSCCAL)
SIM_UNSIMULATED_8(73, CLKPR)
SIM_UNSIMULATED_8(74, EECR)
SIM_UNSIMULATED_8(75, EEDR)
SIM_UNSIMULATED_16(76, EEAR)
SIM_UNSIMULATED_8(77, EEARL)
SIM_UNSIMULATED_8(78, EEARH)
SIM_UNSIMULATED_8(79, GPIOR0)
SIM_UNSIMULATED_8(80, GPIOR1)
SIM_UNSIMULATED_8(81, GPIOR2)
SIM_UNSIMULATED_8(82, SPMCSR)
#undef SIM_UNSIMULATED_8
#undef SIM_UNSIMULATED_16
}  // namespace reg

}  // namespace sim

#define PORTB (::sim::reg::PORTB_)
#define DDRB (::sim::reg::DDRB_)
#define PINB (::sim::reg::PINB_)
#define PORTC (::sim::reg::PORTC_)
#define DDRC (::sim::reg::DDRC_)
#define PINC (::sim::reg::PINC_)
#define PORTD (::sim::reg::PORTD_)
#define DDRD (::sim::reg::DDRD_)
#define PIND (::sim::reg::PIND_)
#define SREG (::sim::reg::SREG_)

#define TCCR0A (::sim::reg::TCCR0A_)
#define TCCR0B (::sim::reg::TCCR0B_)
#define TCNT0 (::sim::reg::TCNT0_)
#define OCR0A (::sim::reg::OCR0A_)
#define OCR0B (::sim::reg::OCR0B_)
#define TIMSK0 (::sim::reg::TIMSK0_)
#define TIFR0 (::sim::reg::TIFR0_)
#define TCCR1A (::sim::reg::TCCR1A_)
#define TCCR1B (::sim::reg::TCCR1B_)
#define TCCR1C (::sim::reg::TCCR1C_)
#define TCNT1 (::sim::reg::TCNT1_)
#define TCNT1L (::sim::reg::TCNT1L_)
#define TCNT1H (::sim::reg::TCNT1H_)
#define OCR1A (::sim::reg::OCR1A_)
#define OCR1AL (::sim::reg::OCR1AL_)
#define OCR1AH (::sim::reg::OCR1AH_)
#define OCR1B (::sim::reg::OCR1B_)
#define OCR1BL (::sim::reg::OCR1BL_)
#define OCR1BH (::sim::reg::OCR1BH_)
#define ICR1 (::sim::reg::ICR1_)
#define ICR1L (::sim::reg::ICR1L_)
#define ICR1H (::sim::reg::ICR1H_)
#define TIMSK1 (::sim::reg::TIMSK1_)
#define TIFR1 (::sim::reg::TIFR1_)
#define TCCR2A (::sim::reg::TCCR2A_)
#define TCCR2B (::sim::reg::TCCR2B_)
#define TCNT2 (::sim::reg::TCNT2_)
#define OCR2A (::sim::reg::OCR2A_)
#define OCR2B (::sim::reg::OCR2B_)
#define TIMSK2 (::sim::reg::TIMSK2_)
#define TIFR2 (::sim::reg::TIFR2_)
#define ASSR (::sim::reg::ASSR_)
#define GTCCR (::sim::reg::GTCCR_)
#define ADMUX (::sim::reg::ADMUX_)
#define ADCSRA (::sim::reg::ADCSRA_)
#define ADCSRB (::sim::reg::ADCSRB_)
#define ADC (::sim::reg::ADC_)
#define ADCW (::sim::reg::ADCW_)
#define ADCL (::sim::reg::ADCL_)
#define ADCH (::sim::reg::ADCH_)
#define DIDR0 (::sim::reg::DIDR0_)
#define DIDR1 (::sim::reg::DIDR1_)
#define ACSR (::sim::reg::ACSR_)
#define UCSR0A (::sim::reg::UCSR0A_)
#define UCSR0B (::sim::reg::UCSR0B_)
#define UCSR0C (::sim::reg::UCSR0C_)
#define UBRR0 (::sim::reg::UBRR0_)
#define UBRR0L (::sim::reg::UBRR0L_)
#define UBRR0H (::sim::reg::UBRR0H_)
#define UDR0 (::sim::reg::UDR0_)
#define SPCR (::sim::reg::SPCR_)
#define SPSR (::sim::reg::SPSR_)
#define SPDR (::sim::reg::SPDR_)
#define TWBR (::sim::reg::TWBR_)
#define TWSR (::sim::reg::TWSR_)
#define TWAR (::sim::reg::TWAR_)
#define TWDR (::sim::reg::TWDR_)
#define TWCR (::sim::reg::TWCR_)
#define TWAMR (::sim::reg::TWAMR_)
#define EICRA (::sim::reg::EICRA_)
#define EIMSK (::sim::reg::EIMSK_)
#define EIFR (::sim::reg::EIFR_)
#define PCICR (::sim::reg::PCICR_)
#define PCIFR (::sim::reg::PCIFR_)
#define PCMSK0 (::sim::reg::PCMSK0_)
#define PCMSK1 (::sim::reg::PCMSK1_)
#define PCMSK2 (::sim::reg::PCMSK2_)
#define WDTCSR (::sim::reg::WDTCSR_)
#define MCUSR (::sim::reg::MCUSR_)
#define MCUCR (::sim::reg::MCUCR_)
#define SMCR (::sim::reg::SMCR_)
#define PRR (::sim::reg::PRR_)
#define OSCCAL (::sim::reg::OSCCAL_)
#define CLKPR (::sim::reg::CLKPR_)
#define EECR (::sim::reg::EECR_)
#define EEDR (::sim::reg::EEDR_)
#define EEAR (::sim::reg::EEAR_)
#define EEARL (::sim::reg::EEARL_)
#define EEARH (::sim::reg::EEARH_)
#define GPIOR0 (::sim::reg::GPIOR0_)
#define GPIOR1 (::sim::reg::GPIOR1_)
#define GPIOR2 (::sim::reg::GPIOR2_)
#define SPMCSR (::sim::reg::SPMCSR_)

#endif /* SIM_REGISTERS_H */
