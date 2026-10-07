#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void *(*get_slot)(void);

static void *check_thread(void *unused) {
    (void)unused;
    void *blocks[64];
    for (int index = 0; index < 64; ++index) {
        blocks[index] = malloc(8);
        if (!blocks[index]) return (void *)1;
        memset(blocks[index], 0xa5, 8);
    }
    for (int index = 0; index < 64; ++index) free(blocks[index]);

    uint64_t *slot = get_slot();
    if (!slot || *slot) {
        fprintf(stderr, "Fresh exception slot is not zero: %llx\n",
                (unsigned long long)(slot ? *slot : 0));
        return (void *)1;
    }
    *slot = UINT64_C(0x123456789abcdef0);
    if (get_slot() != slot || *slot != UINT64_C(0x123456789abcdef0)) return (void *)1;
    *slot = 0;
    return NULL;
}

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    void *library = dlopen(argv[1], RTLD_NOW);
    if (!library) { fprintf(stderr, "%s\n", dlerror()); return 3; }
    get_slot = dlsym(library, "eh_get_exception_ptr");
    if (!get_slot) return 4;
    for (int index = 0; index < 32; ++index) {
        pthread_t thread;
        void *result;
        if (pthread_create(&thread, NULL, check_thread, NULL) || pthread_join(thread, &result)) return 5;
        if (result) return 1;
    }
    dlclose(library);
    puts("PASS fresh exception slots and same-thread state retention");
    return 0;
}
