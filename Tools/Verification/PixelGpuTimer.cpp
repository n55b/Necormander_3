// Test-player-only D3D11 timestamp queries. Results are polled without flushing or waiting for the GPU.
#include <d3d11.h>
#include <atomic>
#include <cstdio>
#include <wrl/client.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"

using Microsoft::WRL::ComPtr;
struct Interval {
    ComPtr<ID3D11Query> clock, begin, end;
    bool pending = false;
};
static IUnityInterfaces* interfaces = nullptr;
static IUnityGraphics* graphics = nullptr;
static ComPtr<ID3D11DeviceContext> context;
static Interval samples[32];
static int nextSlot = 0, openSlot = -1;
static std::atomic<int> microseconds{0};

static void Trace(const char* message) {
    wchar_t path[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"NECRO_GPU_TRACE", path, MAX_PATH)) return;
    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"a") == 0) { fprintf(file, "%s\n", message); fclose(file); }
}
static void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType eventType) {
    if (eventType == kUnityGfxDeviceEventShutdown) {
        for (auto& sample : samples) sample = Interval{};
        context.Reset();
        nextSlot = 0; openSlot = -1;
    }
    if (eventType != kUnityGfxDeviceEventInitialize || graphics->GetRenderer() != kUnityGfxRendererD3D11) return;
    auto* device = interfaces->Get<IUnityGraphicsD3D11>()->GetDevice();
    if (!device) return;
    Trace("D3D11 device acquired");
    device->GetImmediateContext(context.ReleaseAndGetAddressOf());
    for (auto& sample : samples) {
        D3D11_QUERY_DESC desc{D3D11_QUERY_TIMESTAMP_DISJOINT, 0};
        if (FAILED(device->CreateQuery(&desc, sample.clock.ReleaseAndGetAddressOf()))) { context.Reset(); return; }
        desc.Query = D3D11_QUERY_TIMESTAMP;
        if (FAILED(device->CreateQuery(&desc, sample.begin.ReleaseAndGetAddressOf())) ||
            FAILED(device->CreateQuery(&desc, sample.end.ReleaseAndGetAddressOf()))) { context.Reset(); return; }
    }
}
static void UNITY_INTERFACE_API Mark(int eventId) {
    static bool first=true; if (first) { Trace("Render callback received"); first=false; }
    if (!context) return;
    if (eventId == 1) {
        if (openSlot >= 0) return;
        auto& slot = samples[nextSlot];
        if (slot.pending) {
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT clock{};
            UINT64 begin = 0, end = 0;
            constexpr UINT flags = D3D11_ASYNC_GETDATA_DONOTFLUSH;
            if (context->GetData(slot.clock.Get(),&clock,sizeof(clock),flags) != S_OK ||
                context->GetData(slot.begin.Get(),&begin,sizeof(begin),flags) != S_OK ||
                context->GetData(slot.end.Get(),&end,sizeof(end),flags) != S_OK) return;
            if (!clock.Disjoint && clock.Frequency && end > begin)
                microseconds.store(static_cast<int>((end-begin)*1000000.0 / clock.Frequency));
            slot.pending = false;
        }
        context->Begin(slot.clock.Get());
        context->End(slot.begin.Get());
        openSlot = nextSlot;
    } else if (eventId == 2 && openSlot >= 0) {
        auto& slot = samples[openSlot];
        context->End(slot.end.Get());
        context->End(slot.clock.Get());
        slot.pending = true;
        nextSlot = (openSlot + 1) % 32; openSlot = -1;
    }
}
extern "C" {
    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* api) {
        Trace("Plugin loaded"); interfaces=api; graphics=api->Get<IUnityGraphics>();
        graphics->RegisterDeviceEventCallback(DeviceEvent);
        DeviceEvent(kUnityGfxDeviceEventInitialize);
    }
    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
        graphics->UnregisterDeviceEventCallback(DeviceEvent);
        DeviceEvent(kUnityGfxDeviceEventShutdown);
    }
    UnityRenderingEvent UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetFrameMarker() { Trace("Marker requested"); return Mark; }
    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API ReadGpuMicroseconds() { return microseconds.exchange(0); }
}
