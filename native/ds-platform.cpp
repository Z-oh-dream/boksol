// SPDX-License-Identifier: MPL-2.0

#ifdef _WIN32
#include <windows.h>
#else
#include <dlfcn.h>
#endif
#include <chrono>
#include <condition_variable>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <thread>
#include "LAN.h"
#include "Platform.h"

extern melonDS::LAN* gdsLan;
extern std::string gdsBatteryPath;
extern bool gdsStopped;

namespace melonDS::Platform {

void SignalStop(StopReason, void*) { gdsStopped = true; }
std::string GetLocalFilePath(const std::string& path) { return path; }

FileHandle* OpenFile(const std::string& path, FileMode mode) {
    const bool write = (mode & Write) != 0;
    const bool preserve = (mode & (Preserve | NoCreate)) != 0;
#ifdef _WIN32
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, nullptr, 0);
    if (!size) return nullptr;
    std::wstring wide(size, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, wide.data(), size);
    const wchar_t* flags = !write ? L"rb" : (mode & Append) ? L"ab" : preserve ? L"r+b" :
        (mode & Read) ? L"w+b" : L"wb";
    auto* file = _wfopen(wide.c_str(), flags);
    if (!file && write && preserve && !(mode & NoCreate)) file = _wfopen(wide.c_str(), L"w+b");
#else
    const char* flags = !write ? "rb" : (mode & Append) ? "ab" : preserve ? "r+b" :
        (mode & Read) ? "w+b" : "wb";
    auto* file = fopen(path.c_str(), flags);
    if (!file && write && preserve && !(mode & NoCreate)) file = fopen(path.c_str(), "w+b");
#endif
    return reinterpret_cast<FileHandle*>(file);
}

FileHandle* OpenLocalFile(const std::string& path, FileMode mode) { return OpenFile(path, mode); }
bool CloseFile(FileHandle* file) { return fclose(reinterpret_cast<FILE*>(file)) == 0; }
bool FileExists(const std::string& path) {
    auto* file = OpenFile(path, Read);
    return file && CloseFile(file);
}
bool LocalFileExists(const std::string& path) { return FileExists(path); }
bool CheckFileWritable(const std::string& path) {
    auto* file = OpenFile(path, static_cast<FileMode>(Write | Preserve));
    return file && CloseFile(file);
}
bool CheckLocalFileWritable(const std::string& path) { return CheckFileWritable(path); }
bool IsEndOfFile(FileHandle* file) { return feof(reinterpret_cast<FILE*>(file)) != 0; }
bool FileReadLine(char* str, int n, FileHandle* file) { return fgets(str, n, reinterpret_cast<FILE*>(file)); }
u64 FilePosition(FileHandle* file) {
#ifdef _WIN32
    return _ftelli64(reinterpret_cast<FILE*>(file));
#else
    return ftello(reinterpret_cast<FILE*>(file));
#endif
}
bool FileSeek(FileHandle* file, s64 offset, FileSeekOrigin origin) {
    int from = origin == FileSeekOrigin::Start ? SEEK_SET : origin == FileSeekOrigin::Current ? SEEK_CUR : SEEK_END;
#ifdef _WIN32
    return _fseeki64(reinterpret_cast<FILE*>(file), offset, from) == 0;
#else
    return fseeko(reinterpret_cast<FILE*>(file), offset, from) == 0;
#endif
}
void FileRewind(FileHandle* file) { rewind(reinterpret_cast<FILE*>(file)); }
u64 FileRead(void* data, u64 size, u64 count, FileHandle* file) {
    return fread(data, static_cast<size_t>(size), static_cast<size_t>(count), reinterpret_cast<FILE*>(file));
}
bool FileFlush(FileHandle* file) { return fflush(reinterpret_cast<FILE*>(file)) == 0; }
u64 FileWrite(const void* data, u64 size, u64 count, FileHandle* file) {
    return fwrite(data, static_cast<size_t>(size), static_cast<size_t>(count), reinterpret_cast<FILE*>(file));
}
u64 FileWriteFormatted(FileHandle* file, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    int count = vfprintf(reinterpret_cast<FILE*>(file), fmt, args);
    va_end(args);
    return count < 0 ? 0 : count;
}
u64 FileLength(FileHandle* file) {
    auto cursor = FilePosition(file);
    if (!FileSeek(file, 0, FileSeekOrigin::End)) return 0;
    auto size = FilePosition(file);
    FileSeek(file, cursor, FileSeekOrigin::Start);
    return size;
}
void Log(LogLevel, const char*, ...) {}

struct Thread { std::thread value; };
Thread* Thread_Create(std::function<void()> func) { return new Thread{std::thread(std::move(func))}; }
void Thread_Wait(Thread* thread) { if (thread->value.joinable()) thread->value.join(); }
void Thread_Free(Thread* thread) { Thread_Wait(thread); delete thread; }
struct Semaphore {
    std::mutex mutex;
    std::condition_variable ready;
    int count = 0;
};
Semaphore* Semaphore_Create() { return new Semaphore; }
void Semaphore_Free(Semaphore* semaphore) { delete semaphore; }
bool Semaphore_TryWait(Semaphore* semaphore, int timeout) {
    std::unique_lock lock(semaphore->mutex);
    const auto ready = [&] { return semaphore->count > 0; };
    if (timeout < 0) semaphore->ready.wait(lock, ready);
    else if (!semaphore->ready.wait_for(lock, std::chrono::milliseconds(timeout), ready)) return false;
    --semaphore->count;
    return true;
}
void Semaphore_Reset(Semaphore* semaphore) {
    std::lock_guard lock(semaphore->mutex);
    semaphore->count = 0;
}
void Semaphore_Wait(Semaphore* semaphore) { Semaphore_TryWait(semaphore, -1); }
void Semaphore_Post(Semaphore* semaphore, int count) {
    {
        std::lock_guard lock(semaphore->mutex);
        semaphore->count += count;
    }
    if (count == 1) semaphore->ready.notify_one();
    else semaphore->ready.notify_all();
}
struct Mutex { std::mutex value; };
Mutex* Mutex_Create() { return new Mutex; }
void Mutex_Free(Mutex* mutex) { delete mutex; }
void Mutex_Lock(Mutex* mutex) { mutex->value.lock(); }
void Mutex_Unlock(Mutex* mutex) { mutex->value.unlock(); }
bool Mutex_TryLock(Mutex* mutex) { return mutex->value.try_lock(); }
void Sleep(u64 us) { std::this_thread::sleep_for(std::chrono::microseconds(us)); }
u64 GetUSCount() {
    using namespace std::chrono;
    return duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count();
}
u64 GetMSCount() { return GetUSCount() / 1000; }

void WriteNDSSave(const u8* data, u32 size, u32, u32, void*) {
    if (!size || gdsBatteryPath.empty()) return;
    auto* file = OpenFile(gdsBatteryPath, Write);
    if (file) {
        FileWrite(data, 1, size, file);
        CloseFile(file);
    }
}
void WriteGBASave(const u8*, u32, u32, u32, void*) {}
void WriteFirmware(const Firmware&, u32, u32, void*) {}
void WriteDateTime(int, int, int, int, int, int, void*) {}

void MP_Begin(void*) { if (gdsLan) gdsLan->Begin(0); }
void MP_End(void*) { if (gdsLan) gdsLan->End(0); }
int MP_SendPacket(u8* data, int length, u64 ts, void*) { return gdsLan ? gdsLan->SendPacket(0, data, length, ts) : 0; }
int MP_RecvPacket(u8* data, u64* ts, void*) { return gdsLan ? gdsLan->RecvPacket(0, data, ts) : 0; }
int MP_SendCmd(u8* data, int length, u64 ts, void*) { return gdsLan ? gdsLan->SendCmd(0, data, length, ts) : 0; }
int MP_SendReply(u8* data, int length, u64 ts, u16 aid, void*) { return gdsLan ? gdsLan->SendReply(0, data, length, ts, aid) : 0; }
int MP_SendAck(u8* data, int length, u64 ts, void*) { return gdsLan ? gdsLan->SendAck(0, data, length, ts) : 0; }
int MP_RecvHostPacket(u8* data, u64* ts, void*) { return gdsLan ? gdsLan->RecvHostPacket(0, data, ts) : 0; }
u16 MP_RecvReplies(u8* data, u64 ts, u16 aids, void*) { return gdsLan ? gdsLan->RecvReplies(0, data, ts, aids) : 0; }
int Net_SendPacket(u8*, int, void*) { return 0; }
int Net_RecvPacket(u8*, void*) { return 0; }
void Camera_Start(int, void*) {}
void Camera_Stop(int, void*) {}
void Camera_CaptureFrame(int, u32*, int, int, bool, void*) {}
void Mic_Start(void*) {}
void Mic_Stop(void*) {}
int Mic_ReadInput(s16*, int, void*) { return 0; }
AACDecoder* AAC_Init() { return nullptr; }
void AAC_DeInit(AACDecoder*) {}
bool AAC_Configure(AACDecoder*, int, int) { return false; }
bool AAC_DecodeFrame(AACDecoder*, const void*, int, void*, int) { return false; }
bool Addon_KeyDown(KeyType, void*) { return false; }
void Addon_RumbleStart(u32, void*) {}
void Addon_RumbleStop(void*) {}
float Addon_MotionQuery(MotionQueryType, void*) { return 0; }
#ifdef _WIN32
DynamicLibrary* DynamicLibrary_Load(const char* lib) { return reinterpret_cast<DynamicLibrary*>(LoadLibraryA(lib)); }
void DynamicLibrary_Unload(DynamicLibrary* lib) { FreeLibrary(reinterpret_cast<HMODULE>(lib)); }
void* DynamicLibrary_LoadFunction(DynamicLibrary* lib, const char* name) {
    return reinterpret_cast<void*>(GetProcAddress(reinterpret_cast<HMODULE>(lib), name));
}
#else
DynamicLibrary* DynamicLibrary_Load(const char* lib) {
    return reinterpret_cast<DynamicLibrary*>(dlopen(lib, RTLD_NOW | RTLD_LOCAL));
}
void DynamicLibrary_Unload(DynamicLibrary* lib) { dlclose(lib); }
void* DynamicLibrary_LoadFunction(DynamicLibrary* lib, const char* name) { return dlsym(lib, name); }
#endif
}
