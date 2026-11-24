#include <openexr.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>

typedef struct {
    int width, height;
    unsigned char *bgra;
} Image;

typedef struct {
    FILE *file;
    SRWLOCK lock;
    int64_t size;
} Stream;

static int64_t read_file(exr_const_context_t context, void *stream, void *buffer,
                         uint64_t size, uint64_t offset, exr_stream_error_func_ptr_t error)
{
    Stream *input = stream;
    AcquireSRWLockExclusive(&input->lock);
    if (offset > INT64_MAX || _fseeki64(input->file, (int64_t)offset, SEEK_SET) != 0) {
        ReleaseSRWLockExclusive(&input->lock);
        error(context, EXR_ERR_READ_IO, "Cannot seek in the EXR file");
        return -1;
    }
    int64_t received = (int64_t)fread(buffer, 1, (size_t)size, input->file);
    ReleaseSRWLockExclusive(&input->lock);
    return received;
}

static int64_t file_size(exr_const_context_t context, void *stream)
{
    return ((Stream *)stream)->size;
}

static const char *component(const char *name, const char *prefix)
{
    size_t length = strlen(prefix);
    if (!length) return name;
    return strncmp(name, prefix, length) == 0 && name[length] == '.' ? name + length + 1 : "";
}

static int slot(const char *name, const char *mapping)
{
    if (!name[0] || name[1]) return -1;
    char key = name[0];
    if (key >= 'a' && key <= 'z') key -= 'a' - 'A';
    const char *match = strchr(mapping, key);
    return match ? (int)(match - mapping) : -1;
}

static exr_result_t decode_chunk(exr_const_context_t context, int part, const char *prefix,
    const char *mapping, const exr_chunk_info_t *chunk, float *pixels, int width,
    exr_decode_pipeline_t *decoder, int initialized)
{
    exr_result_t result = initialized ? exr_decoding_update(context, part, chunk, decoder) :
        exr_decoding_initialize(context, part, chunk, decoder);
    if (result == EXR_ERR_SUCCESS) {
        for (int i = 0; i < decoder->channel_count; i++) {
            exr_coding_channel_info_t *channel = &decoder->channels[i];
            int index = slot(component(channel->channel_name, prefix), mapping);
            if (index < 0) continue;
            if (channel->x_samples != 1 || channel->y_samples != 1) {
                result = EXR_ERR_FEATURE_NOT_IMPLEMENTED;
                break;
            }
            channel->user_data_type = EXR_PIXEL_FLOAT;
            channel->user_bytes_per_element = sizeof(float);
            channel->user_pixel_stride = 4 * sizeof(float);
            channel->user_line_stride = width * channel->user_pixel_stride;
            channel->decode_to_ptr = (uint8_t *)(pixels + index);
        }
        if (result == EXR_ERR_SUCCESS && !initialized)
            result = exr_decoding_choose_default_routines(context, part, decoder);
        if (result == EXR_ERR_SUCCESS) result = exr_decoding_run(context, part, decoder);
    }
    return result;
}

typedef int (*IsCanceled)(void);

typedef struct {
    exr_const_context_t context;
    int part, width, height, first_y, tile_width, chunk_height, columns, count, tiled;
    const char *prefix, *mapping;
    float *pixels;
    IsCanceled canceled;
    volatile LONG next, result;
} DecodeJob;

static void decode_work(DecodeJob *job)
{
    exr_decode_pipeline_t decoder = EXR_DECODE_PIPELINE_INITIALIZER;
    int initialized = 0;
    exr_result_t result = EXR_ERR_SUCCESS;
    while (!InterlockedCompareExchange(&job->result, 0, 0)) {
        LONG index = InterlockedIncrement(&job->next) - 1;
        if (index >= job->count) break;
        if (job->canceled && job->canceled()) {
            result = EXR_ERR_UNKNOWN;
            break;
        }
        int x = index % job->columns, y = index / job->columns;
        int64_t left = (int64_t)x * job->tile_width, top = (int64_t)y * job->chunk_height;
        exr_chunk_info_t chunk;
        result = job->tiled ? exr_read_tile_chunk_info(job->context, job->part, x, y, 0, 0, &chunk) :
            exr_read_scanline_chunk_info(job->context, job->part, (int)(job->first_y + top), &chunk);
        if (result != EXR_ERR_SUCCESS) break;
        if (chunk.width < 1 || chunk.height < 1 ||
            left + chunk.width > job->width || top + chunk.height > job->height) {
            result = EXR_ERR_INVALID_ARGUMENT;
            break;
        }
        result = decode_chunk(job->context, job->part, job->prefix, job->mapping, &chunk,
            job->pixels + (top * job->width + left) * 4, job->width, &decoder, initialized);
        if (result != EXR_ERR_SUCCESS) break;
        initialized = 1;
    }
    if (result != EXR_ERR_SUCCESS) InterlockedCompareExchange(&job->result, result, 0);
    exr_decoding_destroy(job->context, &decoder);
}

static void CALLBACK decode_worker(PTP_CALLBACK_INSTANCE instance, void *job, PTP_WORK work)
{
    decode_work(job);
}

static unsigned char display_byte(float value)
{
    if (!(value > 0.0f)) return 0;
    if (value >= 1.0f) return 255;
    return (unsigned char)(255.0f * value + 0.5f);
}

#define SRGB_STEPS 16384
static unsigned char srgb_table[SRGB_STEPS + 1];
static INIT_ONCE srgb_initialized = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK init_srgb(PINIT_ONCE once, void *unused, void **context)
{
    for (int i = 0; i <= SRGB_STEPS; i++) {
        float value = (float)i / SRGB_STEPS;
        srgb_table[i] = display_byte(value <= 0.0031308f ? 12.92f * value :
            1.055f * powf(value, 1.0f / 2.4f) - 0.055f);
    }
    return TRUE;
}

static unsigned char srgb(float value)
{
    if (!(value > 0.0f)) return 0;
    if (value >= 1.0f) return 255;
    return srgb_table[(int)(value * SRGB_STEPS + 0.5f)];
}

__declspec(dllexport) int fv_decode_exr(const wchar_t *path, int part, const char *prefix,
    Image *image, IsCanceled canceled)
{
    Stream stream = { _wfopen(path, L"rb"), SRWLOCK_INIT, -1 };
    if (!stream.file) return EXR_ERR_FILE_ACCESS;
    if (_fseeki64(stream.file, 0, SEEK_END) == 0) stream.size = _ftelli64(stream.file);
    exr_context_initializer_t initializer = EXR_DEFAULT_CONTEXT_INITIALIZER;
    initializer.user_data = &stream;
    initializer.read_fn = read_file;
    initializer.size_fn = file_size;
    exr_context_t context = NULL;
    float *pixels = NULL;
    exr_result_t result = exr_start_read(&context, "FrameView EXR", &initializer);
    exr_attr_box2i_t data, display;
    const exr_attr_chlist_t *channels;
    exr_storage_t storage;
    if (result != EXR_ERR_SUCCESS) goto done;
    if ((result = exr_get_storage(context, part, &storage)) != EXR_ERR_SUCCESS ||
        (result = exr_get_data_window(context, part, &data)) != EXR_ERR_SUCCESS ||
        (result = exr_get_display_window(context, part, &display)) != EXR_ERR_SUCCESS ||
        (result = exr_get_channels(context, part, &channels)) != EXR_ERR_SUCCESS) goto done;

    const char *mapping = NULL;
    const char *mappings[] = {"RGBA", "XYZA", "UV A", "Y  A"};
    const unsigned required[] = {7, 7, 3, 1};
    unsigned mask = 0;
    for (int m = 0; m < 4; m++) {
        mask = 0;
        for (int i = 0; i < channels->num_channels; i++) {
            int index = slot(component(channels->entries[i].name.str, prefix), mappings[m]);
            if (index >= 0) mask |= 1u << index;
        }
        if ((mask & required[m]) == required[m]) { mapping = mappings[m]; break; }
    }
    if (!mapping || (storage != EXR_STORAGE_SCANLINE && storage != EXR_STORAGE_TILED)) {
        result = EXR_ERR_FEATURE_NOT_IMPLEMENTED;
        goto done;
    }

    int64_t width = (int64_t)data.max.x - data.min.x + 1;
    int64_t height = (int64_t)data.max.y - data.min.y + 1;
    int64_t display_width = (int64_t)display.max.x - display.min.x + 1;
    int64_t display_height = (int64_t)display.max.y - display.min.y + 1;
    if (width < 1 || height < 1 || display_width < 1 || display_height < 1 ||
        width > INT32_MAX / 16 || display_width > INT32_MAX / 4 ||
        height > INT32_MAX || display_height > INT32_MAX ||
        width * height > INT32_MAX / 16 || display_width * display_height > INT32_MAX / 4) {
        result = EXR_ERR_INVALID_ARGUMENT;
        goto done;
    }
    pixels = calloc((size_t)(width * height) * 4, sizeof(float));
    if (!pixels) { result = EXR_ERR_OUT_OF_MEMORY; goto done; }
    int32_t tile_w = (int32_t)width, tile_h = 0, columns = 1, rows;
    if (storage == EXR_STORAGE_TILED) {
        if ((result = exr_get_tile_sizes(context, part, 0, 0, &tile_w, &tile_h)) != EXR_ERR_SUCCESS ||
            (result = exr_get_tile_counts(context, part, 0, 0, &columns, &rows)) != EXR_ERR_SUCCESS) goto done;
    } else {
        result = exr_get_scanlines_per_chunk(context, part, &tile_h);
        if (result != EXR_ERR_SUCCESS) goto done;
        if (tile_h < 1) { result = EXR_ERR_INVALID_ARGUMENT; goto done; }
        rows = (int)((height + tile_h - 1) / tile_h);
    }
    if (rows < 1 || columns < 1 || tile_w < 1 || tile_h < 1 || (int64_t)rows * columns >= INT32_MAX) {
        result = EXR_ERR_INVALID_ARGUMENT;
        goto done;
    }
    DecodeJob job = { context, part, (int)width, (int)height, data.min.y, tile_w, tile_h, columns,
        rows * columns, storage == EXR_STORAGE_TILED, prefix, mapping, pixels, canceled, 0, 0 };
    PTP_WORK work = NULL;
    if (width * height >= 256 * 256 && job.count >= 4) {
        DWORD processors = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
        unsigned workers = processors >= 16 ? 6 : processors >= 8 ? 4 : processors >= 4 ? 2 : 1;
        uint64_t unpacked_size = 0;
        if (exr_get_chunk_unpacked_size(context, part, &unpacked_size) == EXR_ERR_SUCCESS && unpacked_size > 0) {
            // Allow for multiple frames in flight and additional buffers inside each codec.
            uint64_t budget = 192ull * 1024 * 1024;
            MEMORYSTATUSEX memory = { .dwLength = sizeof(memory) };
            if (GlobalMemoryStatusEx(&memory)) {
                budget = memory.ullTotalPhys / 50;
                if (budget > memory.ullAvailPhys / 8) budget = memory.ullAvailPhys / 8;
                if (budget > 768ull * 1024 * 1024) budget = 768ull * 1024 * 1024;
            }
            uint64_t allowed = budget / unpacked_size;
            if (allowed < workers) workers = allowed > 0 ? (unsigned)allowed : 1;
        }
        if (workers > 1) {
            work = CreateThreadpoolWork(decode_worker, &job, NULL);
            if (work)
                for (unsigned i = 1; i < workers; i++) SubmitThreadpoolWork(work);
        }
    }
    decode_work(&job);
    if (work) {
        WaitForThreadpoolWorkCallbacks(work, TRUE);
        CloseThreadpoolWork(work);
    }
    result = job.result;
    if (result != EXR_ERR_SUCCESS) goto done;

    image->bgra = calloc((size_t)(display_width * display_height), 4);
    if (!image->bgra) { result = EXR_ERR_OUT_OF_MEMORY; goto done; }
    image->width = (int)display_width;
    image->height = (int)display_height;
    InitOnceExecuteOnce(&srgb_initialized, init_srgb, NULL, NULL);
    for (int64_t y = 0; y < height; y++) {
        if ((y % 64) == 0 && canceled && canceled()) {
            result = EXR_ERR_UNKNOWN;
            goto done;
        }
        int64_t dy = y + data.min.y - display.min.y;
        if (dy < 0 || dy >= display_height) continue;
        for (int64_t x = 0; x < width; x++) {
            int64_t dx = x + data.min.x - display.min.x;
            if (dx < 0 || dx >= display_width) continue;
            float *rgba = pixels + (y * width + x) * 4;
            unsigned char *bgra = image->bgra + (dy * display_width + dx) * 4;
            float alpha = mask & 8 ? rgba[3] : 1.0f;
            if (mapping[0] == 'R' || mapping[0] == 'Y') {
                float scale = alpha > 0 && alpha < 1 ? 1.0f / alpha : 1.0f;
                bgra[2] = srgb(rgba[0] * scale);
                bgra[1] = mapping[0] == 'Y' ? bgra[2] : srgb(rgba[1] * scale);
                bgra[0] = mapping[0] == 'Y' ? bgra[2] : srgb(rgba[2] * scale);
            } else {
                bgra[2] = display_byte(rgba[0]);
                bgra[1] = display_byte(rgba[1]);
                bgra[0] = display_byte(rgba[2]);
            }
            bgra[3] = display_byte(alpha);
        }
    }

done:
    free(pixels);
    if (context) exr_finish(&context);
    fclose(stream.file);
    return result;
}

__declspec(dllexport) void fv_free_exr(void *pixels)
{
    free(pixels);
}
