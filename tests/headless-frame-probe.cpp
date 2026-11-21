#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <cstdio>
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")

// A private 64x64 composition swapchain, unattached to any window or desktop.
// Generates real DXGI Present events without opening a window or touching a game.
int WINAPI WinMain(HINSTANCE,HINSTANCE,LPSTR,int) {
    ID3D11Device* device=nullptr;ID3D11DeviceContext* context=nullptr;
    if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)))return 1;
    IDXGIDevice* dxgi=nullptr;device->QueryInterface(__uuidof(IDXGIDevice),(void**)&dxgi);
    IDXGIAdapter* adapter=nullptr;dxgi->GetAdapter(&adapter);
    IDXGIFactory2* factory=nullptr;adapter->GetParent(__uuidof(IDXGIFactory2),(void**)&factory);
    DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=64;desc.Height=64;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;
    desc.Scaling=DXGI_SCALING_STRETCH;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;desc.AlphaMode=DXGI_ALPHA_MODE_PREMULTIPLIED;
    IDXGISwapChain1* swap=nullptr;HRESULT result=factory->CreateSwapChainForComposition(device,&desc,nullptr,&swap);
    if(FAILED(result))return 2;
    ID3D11Texture2D* buffer=nullptr;swap->GetBuffer(0,__uuidof(ID3D11Texture2D),(void**)&buffer);
    ID3D11RenderTargetView* view=nullptr;device->CreateRenderTargetView(buffer,nullptr,&view);buffer->Release();
    wchar_t output[MAX_PATH];GetTempPathW(MAX_PATH,output);wcscat_s(output,L"DesktopIslandHeadlessProbe.txt");
    const auto started=GetTickCount64();int frames=0,successful=0;
    while(GetTickCount64()-started<25000) {
        const float color[]={0.1f,0.1f,0.1f,1.f};context->ClearRenderTargetView(view,color);
        result=swap->Present(0,0);frames++;if(result==S_OK)successful++;
        if(frames%30==0){FILE* file=nullptr;_wfopen_s(&file,output,L"w");if(file){fprintf(file,"pid=%lu frames=%d successful=%d present=%ld average=%.2f\n",GetCurrentProcessId(),frames,successful,result,1000.0*frames/(GetTickCount64()-started));fclose(file);}}
        Sleep(33);
    }
    view->Release();swap->Release();factory->Release();adapter->Release();dxgi->Release();context->Release();device->Release();return 0;
}
