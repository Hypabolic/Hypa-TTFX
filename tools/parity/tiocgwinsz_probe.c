#include <stdio.h>
#include <stddef.h>
#include <sys/ioctl.h>

int main(void)
{
    printf("TIOCGWINSZ=0x%lx\n", (unsigned long)TIOCGWINSZ);
    printf("sizeof_winsize=%lu\n", (unsigned long)sizeof(struct winsize));
    printf("ws_row_off=%lu\n", (unsigned long)offsetof(struct winsize, ws_row));
    printf("ws_col_off=%lu\n", (unsigned long)offsetof(struct winsize, ws_col));
    printf("ws_xpixel_off=%lu\n", (unsigned long)offsetof(struct winsize, ws_xpixel));
    printf("ws_ypixel_off=%lu\n", (unsigned long)offsetof(struct winsize, ws_ypixel));
    return 0;
}
