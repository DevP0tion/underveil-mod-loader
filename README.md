# Underveil Mod Loader

Underveil용 모드 로더이자 인게임 모드 관리자입니다. [BepInEx](https://github.com/BepInEx/BepInEx) 5 플러그인으로 만들었습니다.

비공식 팬 프로젝트로, 게임 개발사와 관련이 없습니다. 이 저장소에는 게임 파일이 들어 있지 않습니다.

Underveil 0.3.7-Demo와 BepInEx 5.4.23.6(Windows x64)에서 시험했습니다.

## 기능

- 게임을 시작할 때 `Mods/<폴더>/`에서 모드를 불러옵니다.
- 의존 모드를 먼저 불러오고, 의존 모드가 없거나 순환 의존이 있으면 알려 줍니다.
- 모드 하나가 실패해도 나머지 모드는 그대로 불러오고, 실패한 모드의 Harmony 패치는 되돌립니다.
- 타이틀 화면에 **모드** 버튼을 추가합니다. 창에서 설치된 모드의 목록을 볼 수 있습니다.
- 모드명 왼쪽의 **i** 버튼을 누르면 창 왼쪽 패널에 모드의 버전, 제작자, 상태, 오류, 설명을 표시합니다.
- 같은 창에서 모드를 켜고 끌 수 있습니다. 이미 불러온 모드는 내릴 수 없으므로, 변경 사항은 게임을 다시 시작한 뒤 적용됩니다.

## 설치

1. BepInEx가 포함된 Release zip을 받습니다.
2. zip 전체를 `Underveil.exe`가 있는 게임 폴더에 풉니다.
3. 게임을 끄고 `install.cmd`를 실행합니다.
4. 게임을 시작합니다. 로더가 `Underveil.exe` 옆에 `Mods` 폴더를 만듭니다.

BepInEx가 이미 설치되어 있으면 그대로 사용합니다. 설치가 끝나면 `install.cmd`와 `UnderveilModLoader_files`는 지워도 됩니다.

## 빌드

.NET SDK와 BepInEx가 설치된 게임이 필요합니다. 참조 어셈블리를 이 게임 폴더에서 가져옵니다. 빌드하면 `UnderveilModLoader.dll`이 `BepInEx/plugins`로 복사됩니다.

```bash
dotnet build -c Release -p:GameDir="C:\path\to\Underveil Demo"
```

## 배포

`Plugin.cs`의 버전을 올려 `main`에 커밋하고 push한 뒤, 게임을 끄고 `release.cmd`를 실행합니다. 이 스크립트는 로더를 빌드하고 BepInEx와 함께 패키징한 뒤 GitHub Release를 게시합니다. push는 하지 않으며, `main`이 GitHub와 다르면 중단합니다. 이 저장소를 게임 폴더의 `_mods_src\UnderveilModLoader`에 두거나 `GameDir` 환경 변수를 설정해야 하고, GitHub CLI에 로그인되어 있어야 합니다.

## 모드 만들기

전체 예시는 [UnderveilExampleMod](https://github.com/DevP0tion/UnderveilExampleMod)를 참고하세요.

모드는 `Mods` 안의 폴더 하나이며, manifest와 DLL로 이루어집니다.

```
Mods/MyMod/
  mod.json
  MyMod.dll
```

```json
{
  "id": "my-mod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "You",
  "description": "모드 창의 설명 패널에 표시되는 설명입니다.",
  "dll": "MyMod.dll",
  "dependencies": []
}
```

`id`와 `dll`은 필수입니다. `description`이 없으면 설명 패널에 "설명 없음"을 표시합니다. DLL에는 `IMod`를 구현한 클래스가 정확히 하나 있어야 합니다.

```csharp
using UnderveilModLoader;

public class MyMod : IMod
{
    public void OnLoad(ModContext context)
    {
        context.Logger.LogInfo("Hello");
        context.Harmony.PatchAll(typeof(MyMod).Assembly);
    }
}
```

`ModContext`는 `Info`, `Logger`, `Harmony`, `Directory`를 제공합니다.

## 설정

창에서 끈 모드는 `Mods/settings.json`에 저장됩니다.

```json
{
  "disabled": ["my-mod"]
}
```

이 파일을 해석할 수 없으면 고칠 때까지 모드를 하나도 불러오지 않습니다. 꺼 둔 모드가 실수로 로드되는 일을 막기 위해서입니다.

## 라이선스

[MIT](LICENSE)
