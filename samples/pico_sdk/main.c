/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * The dual-core checks once more, this time written against the Raspberry
 * Pi Pico SDK instead of the registers. Everything that touches the
 * hardware here is SDK code: its start-up, its clock set-up, its launch of
 * core 1, its mailbox, mutex, doorbell, sleep and lockout functions. If the
 * model and the SDK disagree about the chip, a check fails.
 */

#include <stdio.h>

#include "hardware/irq.h"
#include "pico/multicore.h"
#include "pico/mutex.h"
#include "pico/stdlib.h"

/* Messages over the mailbox: a command in the top byte, the rest is data. */
#define MSG(command, data)	(((uint32_t)(command) << 24) | ((data) & 0x00ffffffu))
#define MSG_COMMAND(message)	((message) >> 24)
#define MSG_DATA(message)	((message) & 0x00ffffffu)

enum {
	CMD_HELLO = 1,	/* core 1 is up; data: its core number */
	CMD_PING,	/* answer with data + 1 */
	CMD_COUNT,	/* count to data under the mutex */
	CMD_BELL,	/* ring the doorbell on core 0 */
	CMD_SLEEP,	/* sleep for data ms and report how long it took */
	CMD_SPIN,	/* become a lockout victim and spin for ever */
};

static mutex_t count_mutex;
static volatile uint32_t shared_count;
static volatile uint32_t bells_rung;
static volatile uint32_t spins;
static volatile uint32_t core1_launches;
static uint doorbell;

/*
 * Increment the shared counter the slow way, so that an increment lost to
 * the other core is likely whenever the mutex does not exclude it.
 */
static void count_under_mutex(uint32_t times)
{
	for (uint32_t i = 0; i < times; i++) {
		mutex_enter_blocking(&count_mutex);
		uint32_t value = shared_count;

		for (volatile int delay = 0; delay < 4; delay++) {
		}
		shared_count = value + 1;
		mutex_exit(&count_mutex);
	}
}

/* -- Core 1 ---------------------------------------------------------------- */

static void core1_main(void)
{
	core1_launches++;
	printf("core 1: running, core number %u\n", get_core_num());
	multicore_fifo_push_blocking(MSG(CMD_HELLO, get_core_num()));

	for (;;) {
		uint32_t message = multicore_fifo_pop_blocking();
		uint32_t data = MSG_DATA(message);

		switch (MSG_COMMAND(message)) {
		case CMD_PING:
			multicore_fifo_push_blocking(MSG(CMD_PING, data + 1));
			break;
		case CMD_COUNT:
			count_under_mutex(data);
			multicore_fifo_push_blocking(MSG(CMD_COUNT, data));
			break;
		case CMD_BELL:
			multicore_doorbell_set_other_core(doorbell);
			break;
		case CMD_SLEEP: {
			uint32_t start = time_us_32();

			sleep_ms(data);
			multicore_fifo_push_blocking(MSG(CMD_SLEEP, time_us_32() - start));
			break;
		}
		case CMD_SPIN:
			/* The lockout takes the mailbox over, so this is the
			 * last command core 1 can take. */
			multicore_lockout_victim_init();
			for (;;) {
				spins++;
			}
		default:
			break;
		}
	}
}

/* -- Core 0 ---------------------------------------------------------------- */

static bool request(uint32_t message, uint32_t *data)
{
	uint32_t answer;

	multicore_fifo_push_blocking(message);
	if (!multicore_fifo_pop_timeout_us(2000000, &answer) ||
	    MSG_COMMAND(answer) != MSG_COMMAND(message)) {
		return false;
	}
	*data = MSG_DATA(answer);
	return true;
}

static bool core1_start(void)
{
	uint32_t hello;

	multicore_launch_core1(core1_main);
	return multicore_fifo_pop_timeout_us(2000000, &hello) && hello == MSG(CMD_HELLO, 1);
}

static void doorbell_handler(void)
{
	if (multicore_doorbell_is_set_current_core(doorbell)) {
		multicore_doorbell_clear_current_core(doorbell);
		bells_rung++;
	}
}

/* Wait until *value is no longer was; false if it stays the same. */
static bool changes(volatile uint32_t *value, uint32_t was)
{
	absolute_time_t deadline = make_timeout_time_ms(1000);

	while (*value == was) {
		if (time_reached(deadline)) {
			return false;
		}
	}
	return true;
}

static bool check_launch(void)
{
	return core1_start() && core1_launches == 1;
}

static bool check_ping_pong(void)
{
	for (uint32_t i = 0; i < 16; i++) {
		uint32_t data;

		if (!request(MSG(CMD_PING, i), &data) || data != i + 1) {
			return false;
		}
	}
	return true;
}

/* The SDK's mutex: a spinlock, and an event to wake whoever waits for it. */
static bool check_mutex(void)
{
	const uint32_t times = 2000;
	uint32_t answer;

	shared_count = 0;
	multicore_fifo_push_blocking(MSG(CMD_COUNT, times));
	count_under_mutex(times);
	if (!multicore_fifo_pop_timeout_us(10000000, &answer) || answer != MSG(CMD_COUNT, times)) {
		return false;
	}
	return shared_count == 2 * times;
}

static bool check_doorbell(void)
{
	uint irq = multicore_doorbell_irq_num(doorbell);

	bells_rung = 0;
	irq_set_exclusive_handler(irq, doorbell_handler);
	irq_set_enabled(irq, true);
	multicore_fifo_push_blocking(MSG(CMD_BELL, 0));

	return changes(&bells_rung, 0) && bells_rung == 1;
}

/* Sleeping on either core ends in a timer alarm that core 0 services. */
static bool check_sleep(void)
{
	const uint32_t ms = 20;
	uint32_t start = time_us_32();
	uint32_t elapsed;

	sleep_ms(ms);
	elapsed = time_us_32() - start;
	if (elapsed < ms * 1000 || elapsed > ms * 1500) {
		return false;
	}

	return request(MSG(CMD_SLEEP, ms), &elapsed) && elapsed >= ms * 1000 && elapsed <= ms * 1500;
}

/* Core 0 stops core 1 in its tracks through core 1's mailbox interrupt. */
static bool check_lockout(void)
{
	uint32_t before;

	multicore_fifo_push_blocking(MSG(CMD_SPIN, 0));
	if (!changes(&spins, 0)) {
		return false;
	}

	multicore_lockout_start_blocking();
	before = spins;
	busy_wait_ms(2);
	if (spins != before) {
		return false;
	}
	multicore_lockout_end_blocking();

	return changes(&spins, before);
}

/* Reset core 1 through the PSM and launch it a second time. */
static bool check_reset(void)
{
	uint32_t data;

	multicore_reset_core1();

	return core1_start() && core1_launches == 2 &&
	       request(MSG(CMD_PING, 41), &data) && data == 42;
}

static const struct {
	const char *name;
	bool (*run)(void);
} checks[] = {
	{ "launch core 1 ............", check_launch },
	{ "mailbox ping-pong ........", check_ping_pong },
	{ "mutex ....................", check_mutex },
	{ "doorbell .................", check_doorbell },
	{ "sleep on both cores ......", check_sleep },
	{ "lockout ..................", check_lockout },
	{ "reset and relaunch .......", check_reset },
};

int main(void)
{
	const unsigned int total = count_of(checks);
	unsigned int failed = 0;

	stdio_init_all();
	mutex_init(&count_mutex);
	doorbell = (uint)multicore_doorbell_claim_unused((1u << NUM_CORES) - 1, true);

	printf("RP2350 dual-core sample, Pico SDK\n");
	printf("core 0: running, core number %u\n", get_core_num());

	for (unsigned int i = 0; i < total; i++) {
		bool ok = checks[i].run();

		printf("%s %s\n", checks[i].name, ok ? "ok" : "FAILED");
		failed += !ok;
	}

	if (failed == 0) {
		printf("all %u checks passed\n", total);
	} else {
		printf("%u of %u checks FAILED\n", failed, total);
	}

	for (;;) {
		tight_loop_contents();
	}
}
