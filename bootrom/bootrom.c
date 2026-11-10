/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * Boot ROM stand-in for the RP2350 Renode model, core 0 side: find an image
 * the way the silicon ROM does, by its IMAGE_DEF block, enter it, and answer
 * the handful of ROM calls that software makes on a system with two cores.
 *
 * This is not the Raspberry Pi boot ROM and shares no code with it. It
 * implements the documented interfaces only as far as the model needs:
 * no signature or hash checks, no partitions, no USB or UART boot.
 */

#include <stdint.h>

#include "bootrom.h"

void rom_enter_image(uint32_t vtor, uint32_t sp, uint32_t pc, uint32_t sp_limit);
void rom_wait_for_vector(void);

/* ------------------------------------------------------------------------ */
/* Image discovery                                                           */
/* ------------------------------------------------------------------------ */

#define PICOBIN_BLOCK_MARKER_START	0xffffded3u
#define PICOBIN_BLOCK_MARKER_END	0xab123579u
#define PICOBIN_MAX_BLOCK_WORDS		(0x280 / 4)
/* The first block of an image has to start within its first 4 kB. */
#define PICOBIN_SEARCH_WORDS		(0x1000 / 4)

#define ITEM_VECTOR_TABLE		0x03
#define ITEM_IMAGE_TYPE			0x42
#define ITEM_ENTRY_POINT		0x44
#define ITEM_LAST			0x7f
#define ITEM_SIZE_IS_TWO_BYTES		0x80

#define IMAGE_TYPE_MASK			0x000f
#define IMAGE_TYPE_EXE			0x0001
#define IMAGE_TYPE_CPU_MASK		0x0700
#define IMAGE_TYPE_CPU_ARM		0x0000
#define IMAGE_TYPE_CHIP_MASK		0x7000
#define IMAGE_TYPE_CHIP_RP2350		0x1000

struct image {
	uint32_t vtor;
	uint32_t sp;
	uint32_t pc;
	uint32_t sp_limit;
};

/*
 * Walk the items of the block starting at blk. Returns nonzero when the
 * block is well formed and describes an Arm executable for this chip.
 */
static int parse_image_def(const uint32_t *blk, uint32_t image_base, struct image *img)
{
	const uint32_t *item = blk + 1;
	uint32_t image_type = 0;
	uint32_t vtor = image_base;
	uint32_t entry_pc = 0;
	uint32_t entry_sp = 0;
	uint32_t sp_limit = 0;
	int have_image_type = 0;
	int have_entry = 0;
	uint32_t words = 0;

	for (;;) {
		uint32_t header = *item;
		uint32_t type = header & 0xff;
		uint32_t size;

		if (type & ITEM_SIZE_IS_TWO_BYTES) {
			size = (header >> 8) & 0xffff;
		} else {
			size = (header >> 8) & 0xff;
		}
		type &= 0x7f;

		if (type == ITEM_LAST) {
			/* Its size field is the length of everything before it,
			 * then come the link to the next block and the end marker. */
			if (size != words || item[2] != PICOBIN_BLOCK_MARKER_END) {
				return 0;
			}
			break;
		}

		if (size == 0 || words + size > PICOBIN_MAX_BLOCK_WORDS) {
			return 0;
		}

		switch (type) {
		case ITEM_IMAGE_TYPE:
			image_type = header >> 16;
			have_image_type = 1;
			break;
		case ITEM_VECTOR_TABLE:
			if (size < 2) {
				return 0;
			}
			vtor = item[1];
			break;
		case ITEM_ENTRY_POINT:
			if (size < 3) {
				return 0;
			}
			entry_pc = item[1];
			entry_sp = item[2];
			sp_limit = (size >= 4) ? item[3] : 0;
			have_entry = 1;
			break;
		default:
			break;
		}

		item += size;
		words += size;
	}

	if (!have_image_type ||
	    (image_type & IMAGE_TYPE_MASK) != IMAGE_TYPE_EXE ||
	    (image_type & IMAGE_TYPE_CPU_MASK) != IMAGE_TYPE_CPU_ARM ||
	    (image_type & IMAGE_TYPE_CHIP_MASK) != IMAGE_TYPE_CHIP_RP2350) {
		return 0;
	}

	img->vtor = vtor;
	img->sp_limit = sp_limit;
	if (have_entry) {
		img->sp = entry_sp;
		img->pc = entry_pc;
	} else {
		const uint32_t *vectors = (const uint32_t *)vtor;

		img->sp = vectors[0];
		img->pc = vectors[1];
	}

	return 1;
}

static int find_image(uint32_t image_base, struct image *img)
{
	const uint32_t *words = (const uint32_t *)image_base;

	for (uint32_t i = 0; i < PICOBIN_SEARCH_WORDS; i++) {
		if (words[i] == PICOBIN_BLOCK_MARKER_START &&
		    parse_image_def(&words[i], image_base, img)) {
			return 1;
		}
	}

	return 0;
}

/*
 * Flash first, as after a power-on reset. Main SRAM is tried second so that
 * a RAM-only image loaded by the simulator boots without further setup; the
 * silicon ROM only does that when asked to through a RAM image reboot.
 */
void rom_boot_core0(void)
{
	struct image img;

	if (find_image(XIP_BASE, &img) || find_image(SRAM_BASE, &img)) {
		rom_enter_image(img.vtor, img.sp, img.pc, img.sp_limit);
	}
}

/* ------------------------------------------------------------------------ */
/* ROM functions                                                             */
/* ------------------------------------------------------------------------ */

#define BOOTROM_OK			0
#define BOOTROM_ERROR_NOT_PERMITTED	(-4)

#define REBOOT_TYPE_MASK		0x000f
#define REBOOT_TYPE_NORMAL		0x0000
#define REBOOT_TYPE_FLASH_UPDATE	0x0004

/* Bootrom state lives in the model, and a reset clears it there. */
void rom_bootrom_state_reset(uint32_t flags)
{
	(void)flags;
}

/* The ROM functions here need no more stack than the caller already has. */
int rom_set_bootrom_stack(void *stack)
{
	(void)stack;

	return BOOTROM_OK;
}

/*
 * Only the reboots that end in the image being started again are honoured,
 * and without the delay. BOOTSEL, RAM image and PC/SP reboots need parts of
 * the chip that are not modelled.
 */
int rom_reboot(uint32_t flags, uint32_t delay_ms, uint32_t p0, uint32_t p1)
{
	uint32_t type = flags & REBOOT_TYPE_MASK;

	(void)delay_ms;
	(void)p0;
	(void)p1;

	if (type != REBOOT_TYPE_NORMAL && type != REBOOT_TYPE_FLASH_UPDATE) {
		return BOOTROM_ERROR_NOT_PERMITTED;
	}

	*(volatile uint32_t *)(SCB_BASE + SCB_AIRCR) = SCB_AIRCR_SYSRESETREQ;
	for (;;) {
	}
}

/* ------------------------------------------------------------------------ */
/* ROM table lookup                                                          */
/* ------------------------------------------------------------------------ */

/*
 * The table (in start.S) has the shape of the silicon one: a two-character
 * code, a flag word saying which variants of the entry exist, then one
 * 16-bit pointer per flag that is set, lowest bit first. A zero code ends it.
 */
extern const uint16_t rom_table[];

/* Returns a pointer to the 16-bit slot holding the value, or NULL. */
const uint16_t *rom_table_lookup_entry(uint32_t code, uint32_t mask)
{
	const uint16_t *entry = rom_table;

	while (entry[0] != 0) {
		uint32_t flags = entry[1];
		uint32_t wanted = flags & mask;
		const uint16_t *value = entry + 2;

		if (entry[0] == code && wanted != 0) {
			/* Skip the variants that sort below the one asked for. */
			uint32_t below = flags & ((wanted & -wanted) - 1);

			for (; below != 0; below &= below - 1) {
				value++;
			}
			return value;
		}

		for (; flags != 0; flags &= flags - 1) {
			value++;
		}
		entry = value;
	}

	return 0;
}

/* Returns the function or data pointer for code, or NULL. */
void *rom_table_lookup(uint32_t code, uint32_t mask)
{
	const uint16_t *value = rom_table_lookup_entry(code, mask);

	return value ? (void *)(uintptr_t)*value : 0;
}
