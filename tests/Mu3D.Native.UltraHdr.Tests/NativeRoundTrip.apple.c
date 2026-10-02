// Headless Apple native smoke test. Use the pinned v2.0.1 header and the same archives as Gallery.
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include "ultrahdr_api.h"

static void check(uhdr_error_info_t error, const char *operation) {
    if (error.error_code != UHDR_CODEC_OK) {
        fprintf(stderr, "%s: %d %s\n", operation, error.error_code, error.has_detail ? error.detail : "");
        exit(1);
    }
}
#define CHECK(call) check((call), #call)

int main(void) {
    enum { width = 32, height = 32 };
    __fp16 pixels[width * height * 4];
    for (int i = 0; i < width * height; ++i) {
        float value = (i % width < 8) ? .02f : (i % width < 16) ? .18f : (i % width < 24) ? 1.f : 4.f;
        pixels[i * 4] = value;
        pixels[i * 4 + 1] = value;
        pixels[i * 4 + 2] = value;
        pixels[i * 4 + 3] = 1.f;
    }
    for (int scale = 1; scale <= 2; ++scale) {
        uhdr_codec_private_t *enc = uhdr_create_encoder(), *dec = uhdr_create_decoder();
        if (!enc || !dec) return 1;
        uhdr_raw_image_t input = {
            .fmt = UHDR_IMG_FMT_64bppRGBAHalfFloat, .cg = UHDR_CG_DISPLAY_P3,
            .ct = UHDR_CT_LINEAR, .range = UHDR_CR_FULL_RANGE, .w = width, .h = height,
            .planes = {pixels, NULL, NULL}, .stride = {width, 0, 0}
        };
        CHECK(uhdr_enc_set_raw_image(enc, &input, UHDR_HDR_IMG));
        CHECK(uhdr_enc_set_output_format(enc, UHDR_CODEC_JPG));
        CHECK(uhdr_enc_set_quality(enc, 95, UHDR_BASE_IMG));
        CHECK(uhdr_enc_set_quality(enc, 95, UHDR_GAIN_MAP_IMG));
        CHECK(uhdr_enc_set_gainmap_scale_factor(enc, scale));
        CHECK(uhdr_enc_set_using_multi_channel_gainmap(enc, scale == 1));
        CHECK(uhdr_enc_set_target_display_peak_brightness(enc, 1000.f));
        CHECK(uhdr_enc_set_preset(enc, scale == 1 ? UHDR_USAGE_BEST_QUALITY : UHDR_USAGE_REALTIME));
        CHECK(uhdr_encode(enc));
        uhdr_compressed_image_t *compressed = uhdr_get_encoded_stream(enc);
        if (!compressed || !compressed->data_sz || !is_uhdr_image(compressed->data, (int)compressed->data_sz)) return 1;
        CHECK(uhdr_dec_set_image(dec, compressed));
        CHECK(uhdr_dec_set_out_img_format(dec, UHDR_IMG_FMT_64bppRGBAHalfFloat));
        CHECK(uhdr_dec_set_out_color_transfer(dec, UHDR_CT_LINEAR));
        CHECK(uhdr_dec_probe(dec));
        int gw = uhdr_dec_get_gainmap_width(dec), gh = uhdr_dec_get_gainmap_height(dec);
        if (gw != width / scale || gh != height / scale) return 1;
        CHECK(uhdr_decode(dec));
        uhdr_raw_image_t *output = uhdr_get_decoded_image(dec);
        if (!output || output->w != width || output->h != height) return 1;
        float peak = 0;
        for (unsigned y = 0; y < output->h; ++y)
            for (unsigned x = 0; x < output->w; ++x) {
                float value = ((__fp16 *)output->planes[0])[(y * output->stride[0] + x) * 4];
                if (!isfinite(value) || value < 0) return 1;
                peak = fmaxf(peak, value);
            }
        if (peak <= 1.f) return 1;
        printf("scale=%d gainmap=%dx%d bytes=%zu decoded HDR peak=%.4f: passed\n", scale, gw, gh, compressed->data_sz, peak);
        uhdr_release_decoder(dec); uhdr_release_encoder(enc);
    }
    return 0;
}
