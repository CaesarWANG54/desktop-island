#include <windows.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include <cstdio>
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"propsys.lib")
#pragma comment(lib,"uuid.lib")
int wmain(int argc,wchar_t**argv){
 if(argc!=3)return 2;CoInitializeEx(0,COINIT_APARTMENTTHREADED);
 IShellLinkW* link=nullptr;auto hr=CoCreateInstance(CLSID_ShellLink,0,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&link));
 if(FAILED(hr))return 3;link->SetPath(argv[1]);link->SetDescription(L"灵动岛");link->SetShowCmd(SW_SHOWNORMAL);
 IPropertyStore* store=nullptr;link->QueryInterface(IID_PPV_ARGS(&store));PROPVARIANT value;InitPropVariantFromString(L"DesktopIsland",&value);store->SetValue(PKEY_AppUserModel_ID,value);store->Commit();PropVariantClear(&value);store->Release();
 IPersistFile* file=nullptr;link->QueryInterface(IID_PPV_ARGS(&file));hr=file->Save(argv[2],TRUE);file->Release();link->Release();CoUninitialize();return FAILED(hr)?4:0;
}
