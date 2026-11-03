// ArduSim (libraries/compat/FastLED/README.md): replaces FastLED 3.9.20's stub clockless controller,
// which drops every frame. Here showPixels() hands the frame, in the controller's color order and
// after brightness, color correction and dithering, to the simulator (ABI 2.3).
#pragma once

#include <Arduino.h>
#include <sim/led_strip.h>

#include "fl/namespace.h"
#include "eorder.h"

FASTLED_NAMESPACE_BEGIN

#define FASTLED_HAS_CLOCKLESS 1

template <int DATA_PIN, int T1, int T2, int T3, EOrder RGB_ORDER = RGB, int XTRA0 = 0, bool FLIP = false, int WAIT_TIME = 0>
class ClocklessController : public CPixelLEDController<RGB_ORDER> {
public:
	virtual void init() {
		pinMode(DATA_PIN, OUTPUT);
	}

protected:
	virtual void showPixels(PixelController<RGB_ORDER> & pixels) {
		static uint8_t* frame = nullptr;
		static int capacity = 0;
		int count = pixels.size();
		if (count * 3 > capacity) {
			free(frame);
			capacity = count * 3;
			frame = static_cast<uint8_t*>(malloc(capacity));
			if (frame == nullptr) {
				capacity = 0;
				return;
			}
		}
		int n = 0;
		pixels.preStepFirstByteDithering();
		while (pixels.has(1)) {
			frame[n++] = pixels.loadAndScale0();
			frame[n++] = pixels.loadAndScale1();
			frame[n++] = pixels.loadAndScale2();
			pixels.advanceData();
			pixels.stepDithering();
		}
		sim_led_strip_write(DATA_PIN, frame, n, 800);
	}
};

FASTLED_NAMESPACE_END