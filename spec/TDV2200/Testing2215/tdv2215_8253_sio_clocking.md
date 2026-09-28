# TDV 2215: Intel 8253 Timer Integration with Z80SIO/2

## Introduction

This document describes the configuration and wiring of the Intel 8253 Programmable Interval Timer (PIT) in the Tandberg TDV 2215 terminal. Specifically, it explains how the 8253 interacts with the Zilog Z80SIO/2 (Serial Input/Output Controller) to generate the clock signals necessary for serial communication (baud rate generation).

Understanding this integration is critical for accurate emulation of the TDV 2215, ensuring correct baud rate timing, serial interrupt behavior, and UART state transitions.

---

## Summary of Observed Configuration

- The **8253 timer** receives its input clock on **CLK0** from a **74LS393 ripple counter**, which divides an external `-CLK` signal.
- The **8253 OUT0** output is routed to **CIA**, which is buffered through a **74LS240** and drives the **Z80SIO/2 TXC and RXC pins** (pins 14 and 13).
- The **Z80SIO/2** is configured in **x16 clock mode** using `WR4 = 0x44`, indicating that TXC and RXC receive a clock 16× the desired baud rate.
- The same `-CLK` signal also directly drives **pin 20 (CLK)** of the Z80SIO/2 to serve as its master system clock.

---

## Block Diagram

```plaintext
       -CLK (e.g., 3.6864 MHz)
           │
           ├─────────────┐
           │             ▼
        Z80SIO/2       74LS393 (U8B)
         CLK (20)        │
                         ▼
                     Q0 (÷2)
                         │
                         ▼
                      8253 (U13)
                    CLK0 ← Q0
                     OUT0 ──▶ CIA → TXC/RXC (Z80SIO/2 pins 14/13)
```

---

## Detailed Signal Path

### 1. Clock Source and Division

- The external signal `-CLK` drives:
  - **Pin 13 (CP)** of U8B (first half of 74LS393 dual counter).
  - **Pin 20** of the Z80SIO/2, which serves as its internal clock input.

- The **Q0 output of U8B** divides `-CLK` by 2 and feeds:
  - **CLK0** on the 8253 (U13).

### 2. 8253 Configuration

- The 8253 is programmed by the CPU via standard I/O writes.
- In your captured logs:
  - **Divisor = 48** (for Counter 0)
  - Control word `0x36` (Mode 3: square wave)
- Resulting output from OUT0 = `CLK0 / 48`
  - With `CLK0 = 1.8432 MHz`, OUT0 = 38400 Hz

### 3. Connection to Z80SIO/2

- OUT0 is buffered via a 74LS240 gate and routed to **CIA**, which drives:
  - **TXCA (pin 14)** and **RXCA (pin 13)** on Z80SIO/2
- The Z80SIO/2 is programmed with:
  - **WR4 = 0x44**, enabling **x16 clock mode**
  - Effective baud rate = OUT0 / 16 = 2400 baud

---

## Timing Formula

Given:
- `f(-CLK)` = base clock input
- `f(CLK0)` = `f(-CLK) / 2` (due to ÷2 via 74LS393)
- `f(OUT0)` = `f(CLK0) / divisor` = `f(-CLK) / (2 × divisor)`
- `baud` = `f(OUT0) / 16` (Z80SIO in x16 mode)

Then:
```math
baud = f(-CLK) / (2 × divisor × 16)
```

For 2400 baud and divisor = 48:
```math
f(-CLK) = 2 × 48 × 16 × 2400 = 3.6864 MHz
```

---

## Conclusion

To accurately emulate the TDV 2215:

- Simulate the 8253 PIT receiving a `1.8432 MHz` clock on **CLK0** (derived from `-CLK / 2`)
- Program counter 0 in **mode 3** with a **divisor of 48**
- Route the **OUT0 signal** to the Z80SIO/2's **TXC/RXC pins**
- Ensure the Z80SIO/2 operates in **x16 external clock mode** (`WR4 = 0x44`)
- The `-CLK` signal must also drive **pin 20 (CLK)** on the SIO, as it controls internal state timing

This setup generates a **2400 baud** UART clock and matches observed hardware behavior.

---