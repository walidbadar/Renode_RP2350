/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * Dual-core sample for the RP2350: core 0 brings the chip up, launches
 * core 1 through the boot ROM and then checks, one mechanism at a time,
 * everything two cores need from the hardware to work together. Each check
 * prints a line on UART0; the last line gives the verdict.
 *
 * It uses no SDK and no C library, only the registers in rp2350.h.
 */

#include <stdarg.h>
#include <stdint.h>

#include "rp2350.h"

extern const uint32_t __vectors[];

/* Messages over the mailbox: a command in the top nibble, the rest is data. */
#define MSG(command, data)	((command) | ((data) & 0x0fffffffu))
#define MSG_COMMAND(message)	((message) & 0xf0000000u)
#define MSG_DATA(message)	((message) & 0x0fffffffu)

#define CMD_HELLO	0x10000000u	/* core 1 is up; data: its CPUID */
#define CMD_PING	0x20000000u	/* answer with data + 1 */
#define CMD_HOLD	0x30000000u	/* stop listening until a doorbell rings */
#define CMD_COUNT	0x40000000u	/* count to data under the spinlock */
#define CMD_BELL	0x50000000u	/* ring the doorbells in data on core 0 */
#define CMD_SYSTICK	0x60000000u	/* start SysTick; data: period in us */
#define CMD_TICKS	0x70000000u	/* stop SysTick and report its ticks */
#define CMD_ALARM	0x80000000u	/* take a timer alarm in data us */

#define LOCK_PRINT	16
#define LOCK_COUNT	17

#define SYS_CLK_MHZ	150

static volatile uint32_t shared_count;
static volatile uint32_t count_target;
static volatile uint32_t bells_seen;
static volatile uint32_t ticks[2];
static volatile uint32_t core1_launches;
static volatile uint32_t alarm_set_at;

static uint32_t core1_stack[1024];

/* -- Processor ------------------------------------------------------------- */

static inline uint32_t cpuid(void)
{
	return REG32(SIO_CPUID);
}

static inline uint32_t time_us(void)
{
	return REG32(TIMER_TIMERAWL);
}

static inline void irq_enable(unsigned int irq)
{
	REG32(NVIC_ISER(irq / 32)) = 1u << (irq % 32);
}

static inline void irq_disable(unsigned int irq)
{
	REG32(NVIC_ICER(irq / 32)) = 1u << (irq % 32);
}

static inline uint32_t irq_save(void)
{
	uint32_t primask;

	__asm__ volatile("mrs %0, primask\n\tcpsid i" : "=r"(primask));
	return primask;
}

static inline void irq_restore(uint32_t primask)
{
	__asm__ volatile("msr primask, %0" : : "r"(primask));
}

/* -- Spinlocks ------------------------------------------------------------- */

/* Reading a spinlock register claims it and returns nonzero on success. */
static uint32_t spin_lock(unsigned int lock)
{
	uint32_t primask = irq_save();

	while (REG32(SIO_SPINLOCK(lock)) == 0) {
	}
	return primask;
}

static void spin_unlock(unsigned int lock, uint32_t primask)
{
	REG32(SIO_SPINLOCK(lock)) = 0;
	irq_restore(primask);
}

/* -- Console --------------------------------------------------------------- */

static void put_char(char c)
{
	while (REG32(UART_FR) & UART_FR_TXFF) {
	}
	REG32(UART_DR) = (uint32_t)c;
}

static void put_unsigned(uint32_t value)
{
	char digits[10];
	int n = 0;

	do {
		digits[n++] = (char)('0' + value % 10);
		value /= 10;
	} while (value != 0);
	while (n > 0) {
		put_char(digits[--n]);
	}
}

static void put_hex(uint32_t value)
{
	for (int shift = 28; shift >= 0; shift -= 4) {
		put_char("0123456789abcdef"[(value >> shift) & 0xf]);
	}
}

/* printf with %s, %u and %x; a line is printed whole, whichever core asks. */
static void print(const char *format, ...)
{
	uint32_t primask = spin_lock(LOCK_PRINT);
	va_list args;

	va_start(args, format);
	for (; *format != '\0'; format++) {
		if (*format != '%') {
			if (*format == '\n') {
				put_char('\r');
			}
			put_char(*format);
			continue;
		}
		switch (*++format) {
		case 's':
			for (const char *s = va_arg(args, const char *); *s != '\0'; s++) {
				put_char(*s);
			}
			break;
		case 'u':
			put_unsigned(va_arg(args, unsigned int));
			break;
		case 'x':
			put_hex(va_arg(args, unsigned int));
			break;
		default:
			put_char(*format);
			break;
		}
	}
	va_end(args);
	spin_unlock(LOCK_PRINT, primask);
}

/* -- Mailbox --------------------------------------------------------------- */

static void fifo_push(uint32_t value)
{
	while ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_RDY) == 0) {
	}
	REG32(SIO_FIFO_WR) = value;
	/* The other core may be asleep waiting for this. */
	__asm__ volatile("sev");
}

static void fifo_drain(void)
{
	while ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_VLD) != 0) {
		(void)REG32(SIO_FIFO_RD);
	}
}

/* Returns 0 if nothing arrived in time. */
static int fifo_pop(uint32_t *value, uint32_t timeout_us)
{
	uint32_t start = time_us();

	while ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_VLD) == 0) {
		if (time_us() - start > timeout_us) {
			return 0;
		}
	}
	*value = REG32(SIO_FIFO_RD);
	return 1;
}

/* Send a command and wait for the answer that carries the same command. */
static int request(uint32_t message, uint32_t *data)
{
	uint32_t answer;

	fifo_push(message);
	if (!fifo_pop(&answer, 1000000) || MSG_COMMAND(answer) != MSG_COMMAND(message)) {
		return 0;
	}
	*data = MSG_DATA(answer);
	return 1;
}

/* -- Bring-up -------------------------------------------------------------- */

static void unreset(uint32_t blocks)
{
	REG_CLR(RESETS_RESET) = blocks;
	while ((REG32(RESETS_RESET_DONE) & blocks) != blocks) {
	}
}

/* Crystal, then the system PLL at 150 MHz, then the clocks that hang off it. */
static void clocks_init(void)
{
	unreset(RESET_IO_BANK0 | RESET_PADS_BANK0 | RESET_PLL_SYS);

	REG32(XOSC_CTRL) = XOSC_CTRL_RANGE_1_15MHZ;
	REG32(XOSC_STARTUP) = 47;
	REG_SET(XOSC_CTRL) = XOSC_CTRL_ENABLE;
	while ((REG32(XOSC_STATUS) & XOSC_STATUS_STABLE) == 0) {
	}

	/* Run everything from the crystal while the PLL is being set up. */
	REG_CLR(CLK_SYS_CTRL) = CLK_SYS_CTRL_SRC_AUX;
	while (REG32(CLK_SYS_SELECTED) != 1u << 0) {
	}
	REG32(CLK_REF_CTRL) = CLK_REF_CTRL_SRC_XOSC;
	while (REG32(CLK_REF_SELECTED) != 1u << CLK_REF_CTRL_SRC_XOSC) {
	}

	/* 12 MHz x 125 = 1500 MHz, divided by 5 and by 2. */
	REG32(PLL_SYS_BASE + PLL_CS) = 1;
	REG32(PLL_SYS_BASE + PLL_FBDIV_INT) = 125;
	REG_CLR(PLL_SYS_BASE + PLL_PWR) = PLL_PWR_PD | PLL_PWR_VCOPD;
	while ((REG32(PLL_SYS_BASE + PLL_CS) & PLL_CS_LOCK) == 0) {
	}
	REG32(PLL_SYS_BASE + PLL_PRIM) = (5u << 16) | (2u << 12);
	REG_CLR(PLL_SYS_BASE + PLL_PWR) = PLL_PWR_POSTDIVPD;

	REG32(CLK_SYS_DIV) = 1u << 16;
	REG32(CLK_SYS_CTRL) = CLK_SYS_CTRL_AUXSRC_PLL_SYS;
	REG_SET(CLK_SYS_CTRL) = CLK_SYS_CTRL_SRC_AUX;
	while (REG32(CLK_SYS_SELECTED) != 1u << CLK_SYS_CTRL_SRC_AUX) {
	}

	REG32(CLK_PERI_CTRL) = CLK_PERI_CTRL_ENABLE | CLK_PERI_CTRL_AUXSRC_CLK_SYS;

	/* The timer counts ticks, not clock cycles: one per 12 cycles of the
	 * 12 MHz reference makes them microseconds. */
	REG32(TICKS_TIMER0_CYCLES) = 12;
	REG32(TICKS_TIMER0_CTRL) = TICKS_CTRL_ENABLE;

	unreset(RESET_TIMER0 | RESET_UART0);
}

/* UART0 on GPIO0 (TX) and GPIO1 (RX), 115200 8N1. */
static void uart_init(void)
{
	REG32(IO_BANK0_GPIO_CTRL(0)) = GPIO_FUNC_UART;
	REG32(IO_BANK0_GPIO_CTRL(1)) = GPIO_FUNC_UART;
	REG_SET(PADS_BANK0_GPIO(1)) = PADS_IE;
	REG_CLR(PADS_BANK0_GPIO(0)) = PADS_ISO;
	REG_CLR(PADS_BANK0_GPIO(1)) = PADS_ISO;

	/* 150 MHz / (16 x 115200) = 81.38 */
	REG32(UART_IBRD) = 81;
	REG32(UART_FBRD) = 24;
	REG32(UART_LCR_H) = UART_LCR_H_WLEN_8 | UART_LCR_H_FEN;
	REG32(UART_CR) = UART_CR_UARTEN | UART_CR_TXE | UART_CR_RXE;
}

/* -- Shared work ----------------------------------------------------------- */

/*
 * Increment the shared counter the slow way, so that an increment lost to
 * the other core is likely whenever the lock does not exclude it.
 */
static void count_under_lock(uint32_t times)
{
	for (uint32_t i = 0; i < times; i++) {
		uint32_t primask = spin_lock(LOCK_COUNT);
		uint32_t value = shared_count;

		for (volatile int delay = 0; delay < 4; delay++) {
		}
		shared_count = value + 1;
		spin_unlock(LOCK_COUNT, primask);
	}
}

static void systick_start(uint32_t period_us)
{
	REG32(SYST_RVR) = period_us * SYS_CLK_MHZ - 1;
	REG32(SYST_CVR) = 0;
	REG32(SYST_CSR) = SYST_CSR_CLKSOURCE | SYST_CSR_TICKINT | SYST_CSR_ENABLE;
}

static void systick_stop(void)
{
	REG32(SYST_CSR) = 0;
}

/* -- Interrupt handlers ---------------------------------------------------- */

void fault_handler(void)
{
	for (;;) {
	}
}

void unexpected_irq(void)
{
	for (;;) {
	}
}

/* Each core has its own SysTick, and counts its own ticks. */
void systick_handler(void)
{
	ticks[cpuid()]++;
}

/* Only core 1 enables this one: it is how core 0 gives it work. */
void sio_fifo_handler(void)
{
	while ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_VLD) != 0) {
		uint32_t message = REG32(SIO_FIFO_RD);
		uint32_t data = MSG_DATA(message);

		switch (MSG_COMMAND(message)) {
		case CMD_PING:
			fifo_push(MSG(CMD_PING, data + 1));
			break;
		case CMD_HOLD:
			irq_disable(IRQ_SIO_FIFO);
			fifo_push(MSG(CMD_HOLD, 0));
			return;
		case CMD_COUNT:
			/* Too long for a handler: core1_main() does it. */
			count_target = data;
			break;
		case CMD_BELL:
			REG32(SIO_DOORBELL_OUT_SET) = data;
			break;
		case CMD_SYSTICK:
			ticks[1] = 0;
			systick_start(data);
			fifo_push(MSG(CMD_SYSTICK, 0));
			break;
		case CMD_TICKS:
			systick_stop();
			fifo_push(MSG(CMD_TICKS, ticks[1]));
			break;
		case CMD_ALARM:
			irq_enable(IRQ_TIMER0_ALARM(1));
			REG_SET(TIMER_INTE) = 1u << 1;
			alarm_set_at = time_us();
			REG32(TIMER_ALARM(1)) = alarm_set_at + data;
			break;
		default:
			break;
		}
	}
}

/* Core 0 collects the doorbells; on core 1 a doorbell ends a CMD_HOLD. */
void sio_bell_handler(void)
{
	uint32_t bells = REG32(SIO_DOORBELL_IN_CLR);

	REG32(SIO_DOORBELL_IN_CLR) = bells;
	if (cpuid() == 0) {
		bells_seen |= bells;
	} else {
		irq_enable(IRQ_SIO_FIFO);
	}
}

/* The line goes to both cores; whichever enabled it reports who it was. */
void timer_alarm1_handler(void)
{
	uint32_t elapsed = time_us() - alarm_set_at;

	REG32(TIMER_INTR) = 1u << 1;
	fifo_push(MSG(CMD_ALARM, (cpuid() << 24) | (elapsed & 0xffffff)));
}

/* -- Core 1 ---------------------------------------------------------------- */

static void core1_main(void)
{
	core1_launches++;
	print("core 1: running, CPUID %u\n", (unsigned int)cpuid());

	irq_enable(IRQ_SIO_FIFO);
	irq_enable(IRQ_SIO_BELL);
	fifo_push(MSG(CMD_HELLO, cpuid()));

	for (;;) {
		uint32_t primask = irq_save();
		uint32_t target = count_target;

		if (target == 0) {
			/* A pending interrupt ends the wait even while masked,
			 * so a command cannot slip in between check and sleep. */
			__asm__ volatile("wfi");
			irq_restore(primask);
			continue;
		}
		count_target = 0;
		irq_restore(primask);

		count_under_lock(target);
		fifo_push(MSG(CMD_COUNT, target));
	}
}

/*
 * The boot ROM holds core 1 until it has been sent, and has echoed, this
 * sequence. Any wrong echo means the two sides are out of step, and the
 * zeroes at the start are there to get them back in step.
 */
static int core1_launch(void (*entry)(void), uint32_t *stack_top)
{
	const uint32_t sequence[] = {
		0, 0, 1, (uint32_t)__vectors, (uint32_t)stack_top, (uint32_t)entry,
	};
	const unsigned int length = sizeof(sequence) / sizeof(sequence[0]);
	uint32_t start = time_us();
	unsigned int i = 0;

	while (i < length) {
		uint32_t word = sequence[i];
		uint32_t echo;

		if (time_us() - start > 1000000) {
			return 0;
		}
		if (word == 0) {
			fifo_drain();
			__asm__ volatile("sev");
		}
		fifo_push(word);
		if (!fifo_pop(&echo, 100000)) {
			return 0;
		}
		i = (echo == word) ? i + 1 : 0;
	}

	return 1;
}

static int core1_start(void)
{
	uint32_t hello;

	if (!core1_launch(core1_main, core1_stack + sizeof(core1_stack) / sizeof(core1_stack[0]))) {
		return 0;
	}
	return fifo_pop(&hello, 1000000) && hello == MSG(CMD_HELLO, 1);
}

/* -- Checks ---------------------------------------------------------------- */

static int check_launch(void)
{
	return core1_start() && core1_launches == 1;
}

/* Core 1 answers each ping from its mailbox interrupt. */
static int check_ping_pong(void)
{
	for (uint32_t i = 0; i < 16; i++) {
		uint32_t data;

		if (!request(MSG(CMD_PING, i), &data) || data != i + 1) {
			return 0;
		}
	}

	return 1;
}

/* Depth, the ready and valid flags, and the two sticky error flags. */
static int check_fifo_flags(void)
{
	uint32_t data;

	if (REG32(SIO_FIFO_ST) != SIO_FIFO_ST_RDY) {
		return 0;
	}

	/* Reading an empty mailbox is flagged until the flag is written. */
	(void)REG32(SIO_FIFO_RD);
	if ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_ROE) == 0) {
		return 0;
	}
	REG32(SIO_FIFO_ST) = SIO_FIFO_ST_ROE;
	if ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_ROE) != 0) {
		return 0;
	}

	/* With core 1 not listening, the mailbox takes exactly as many words
	 * as it is deep. */
	if (!request(MSG(CMD_HOLD, 0), &data)) {
		return 0;
	}
	for (uint32_t i = 0; i < SIO_FIFO_DEPTH; i++) {
		if ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_RDY) == 0) {
			return 0;
		}
		REG32(SIO_FIFO_WR) = MSG(CMD_PING, 100 + i);
	}
	if ((REG32(SIO_FIFO_ST) & (SIO_FIFO_ST_RDY | SIO_FIFO_ST_WOF)) != 0) {
		return 0;
	}

	/* One more is dropped and flagged. */
	REG32(SIO_FIFO_WR) = MSG(CMD_PING, 200);
	if ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_WOF) == 0) {
		return 0;
	}
	REG32(SIO_FIFO_ST) = SIO_FIFO_ST_WOF;
	if ((REG32(SIO_FIFO_ST) & SIO_FIFO_ST_WOF) != 0) {
		return 0;
	}

	/* A doorbell gets core 1 listening again: an answer to each, in order. */
	REG32(SIO_DOORBELL_OUT_SET) = 1u << 0;
	for (uint32_t i = 0; i < SIO_FIFO_DEPTH; i++) {
		uint32_t answer;

		if (!fifo_pop(&answer, 1000000) || answer != MSG(CMD_PING, 100 + i + 1)) {
			return 0;
		}
	}

	return !fifo_pop(&data, 10000);
}

/* Both cores hammer one counter; no increment may be lost. */
static int check_spinlock(void)
{
	const uint32_t times = 5000;
	uint32_t primask;
	uint32_t answer;
	int held;

	/* A lock that is held reads as zero and shows in the status word. */
	primask = spin_lock(LOCK_COUNT);
	held = (REG32(SIO_SPINLOCK_ST) & (1u << LOCK_COUNT)) != 0 &&
	       REG32(SIO_SPINLOCK(LOCK_COUNT)) == 0;
	spin_unlock(LOCK_COUNT, primask);
	if (!held || (REG32(SIO_SPINLOCK_ST) & (1u << LOCK_COUNT)) != 0) {
		return 0;
	}

	shared_count = 0;
	fifo_push(MSG(CMD_COUNT, times));
	count_under_lock(times);
	if (!fifo_pop(&answer, 10000000) || answer != MSG(CMD_COUNT, times)) {
		return 0;
	}

	return shared_count == 2 * times;
}

/* Core 1 rings; core 0 takes the doorbell interrupt. */
static int check_doorbell(void)
{
	const uint32_t bells = 0xa5;
	uint32_t start = time_us();

	bells_seen = 0;
	irq_enable(IRQ_SIO_BELL);
	fifo_push(MSG(CMD_BELL, bells));
	while (bells_seen != bells) {
		if (time_us() - start > 1000000) {
			return 0;
		}
	}

	return 1;
}

/* 1 ms ticks on core 0 against 2 ms ticks on core 1, over 40 ms. */
static int check_systick(void)
{
	const uint32_t periods = 40;
	uint32_t data;
	uint32_t start;
	uint32_t elapsed;

	ticks[0] = 0;
	if (!request(MSG(CMD_SYSTICK, 2000), &data)) {
		return 0;
	}
	start = time_us();
	systick_start(1000);
	while (ticks[0] < periods) {
		if (time_us() - start > 1000000) {
			return 0;
		}
	}
	elapsed = time_us() - start;
	systick_stop();

	if (!request(MSG(CMD_TICKS, 0), &data)) {
		return 0;
	}

	/* SysTick and the timer run from different clocks and must agree. */
	return elapsed >= 39000 && elapsed <= 41000 && data >= periods / 2 - 2 && data <= periods / 2 + 2;
}

/* The alarm interrupt is wired to both cores; core 1 claims this one. */
static int check_alarm(void)
{
	const uint32_t delay_us = 500;
	uint32_t answer;
	uint32_t elapsed;

	fifo_push(MSG(CMD_ALARM, delay_us));
	if (!fifo_pop(&answer, 1000000) || MSG_COMMAND(answer) != CMD_ALARM) {
		return 0;
	}
	elapsed = answer & 0xffffff;

	return (MSG_DATA(answer) >> 24) == 1 && elapsed >= delay_us && elapsed < 10 * delay_us;
}

/* Hold core 1 in reset, let it go, and launch it a second time. */
static int check_reset(void)
{
	uint32_t hello;
	uint32_t data;

	REG_SET(PSM_FRCE_OFF) = PSM_FRCE_OFF_PROC1;
	while ((REG32(PSM_FRCE_OFF) & PSM_FRCE_OFF_PROC1) == 0) {
	}
	REG_CLR(PSM_FRCE_OFF) = PSM_FRCE_OFF_PROC1;

	/* Back in the boot ROM, core 1 says so with a zero. */
	if (!fifo_pop(&hello, 1000000) || hello != 0) {
		return 0;
	}

	return core1_start() && core1_launches == 2 &&
	       request(MSG(CMD_PING, 41), &data) && data == 42;
}

/* -- Core 0 ---------------------------------------------------------------- */

static const struct {
	const char *name;
	int (*run)(void);
} checks[] = {
	{ "launch core 1 ............", check_launch },
	{ "mailbox ping-pong ........", check_ping_pong },
	{ "mailbox flags ............", check_fifo_flags },
	{ "spinlock .................", check_spinlock },
	{ "doorbell .................", check_doorbell },
	{ "per-core SysTick .........", check_systick },
	{ "timer alarm on core 1 ....", check_alarm },
	{ "reset and relaunch .......", check_reset },
};

int main(void)
{
	const unsigned int total = sizeof(checks) / sizeof(checks[0]);
	unsigned int failed = 0;

	clocks_init();
	uart_init();

	print("RP2350 dual-core sample\n");
	print("core 0: running, CPUID %u\n", (unsigned int)cpuid());

	for (unsigned int i = 0; i < total; i++) {
		int ok = checks[i].run();

		print("%s %s\n", checks[i].name, ok ? "ok" : "FAILED");
		failed += !ok;
	}

	if (failed == 0) {
		print("all %u checks passed\n", total);
	} else {
		print("%u of %u checks FAILED\n", failed, total);
	}

	return 0;
}
