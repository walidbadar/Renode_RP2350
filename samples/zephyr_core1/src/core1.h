/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 */

#ifndef CORE1_H
#define CORE1_H

#include <stdbool.h>
#include <stdint.h>

/* What core 1 sends once it runs, with its CPUID in the low bits. */
#define CORE1_HELLO	0xc0de0000u

/*
 * Take core 1 out of the boot ROM and into the worker. Returns false if the
 * ROM does not answer. Call it before the mailbox driver is enabled: the
 * launch sequence goes over the same FIFO.
 */
bool core1_launch(void);

#endif /* CORE1_H */
