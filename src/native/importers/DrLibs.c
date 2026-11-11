/* dr_wav and dr_flac's implementations (ADR 0083), built as a third-party
   library: their warnings are their own. No stdio: sounds come from memory.
   Allocations go through the callbacks each call is given (Sound.cpp). */

#define DR_WAV_IMPLEMENTATION
#define DR_WAV_NO_STDIO
#define DR_FLAC_IMPLEMENTATION
#define DR_FLAC_NO_STDIO
#include <dr_wav.h>
#include <dr_flac.h>
