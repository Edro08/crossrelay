# CrossRelay

Aplicación de transferencia directa de archivos punto a punto (P2P) en red local, diseñada con arquitectura **Zero-Trust**, streaming asíncrono de alto rendimiento y control total sobre los dispositivos vinculados en **Windows**, **Linux** y **Android**.

---

## ✨ Características Principales

### 🔒 Seguridad y Emparejamiento (Zero-Trust)
- **Cifrado obligatorio (TLS 1.3)**: Todo el tráfico de control y datos viaja cifrado de extremo a extremo mediante certificados efímeros autofirmados (`ECDSA P-256`).
- **Desafío de vinculación por PIN (Anti-fatiga)**: Estar en la misma red Wi-Fi no autoriza el envío de archivos. Requiere iniciar una solicitud de conexión e introducir activamente un PIN aleatorio de 6 dígitos que expira en 60 segundos.
- **Tolerancia a fuerza bruta**: Límite de 3 intentos fallidos consecutivos de PIN antes de aplicar cuarentena temporal o bloqueo.
- **Control de visibilidad mDNS**: Modos configurables entre *Visible para todos*, *Solo vinculados* o *Modo sigilo*.
- **Lista negra de nodos**: Bloqueo de equipos para descartar silenciosamente cualquier solicitud entrante.

### ⚡ Transferencia y Streaming
- **Streaming directo (Zero-Copy I/O)**: Envío y recepción continua por bloques de 512 KB a 1 MB sin cargar archivos completos en memoria RAM.
- **Integridad con SHA-256 al vuelo**: Verificación criptográfica mientras se escribe en disco. Los archivos en curso usan extensión `.crossrelay.part` y se renombran atómicamente al completar el 100%.
- **Lotes masivos**: Multiplexación para transferir directorios completos o miles de archivos sin penalización por latencia HTTP repetida.
- **Control de ancho de banda (Token Bucket)**: Limitador configurable en tiempo real (*Sin límite*, *100 MB/s*, *50 MB/s*, *10 MB/s*).

### 🖥️ Gestión de Vínculos y Telemetría
- **Políticas por dispositivo**: `AutoAccept` (descarga automática para equipos propios) o `AskConfirmation` (confirmación manual antes de recibir).
- **Carpetas dedicadas y políticas de duplicados**: Rutas de descarga personalizadas por equipo y manejo configurable de colisiones (`Renombrar`, `Sobrescribir`, `Omitir`).
- **Métricas e historial en tiempo real**: Latencia (RTT), velocidad instantánea, progreso por SSE (`/local/v1/events`) y diagnóstico de puertos.

---

## 🏛️ Arquitectura y Estructura

| Componente | Ruta | Stack | Descripción |
| :--- | :--- | :--- | :--- |
| **Core Compartido** | [`core/`](core/) | Go (Stdlib) + `gomobile` | Motor P2P TLS 1.3, mDNS, SSE, IPC local (`/local/v1/*`) y librería `crossrelay.aar` para Android. |
| **Cliente Desktop** | [`desktop/`](desktop/) | C# · Avalonia UI + SkiaSharp | Interfaz nativa para Windows y Linux con tema oscuro Pure OLED (`#000000`). |
| **Cliente Android** | [`android/`](android/) | Kotlin · Jetpack Compose | App nativa Android (API 35+) con `ForegroundService`, `MANAGE_EXTERNAL_STORAGE` y `libgojni.so`. |
| **Scripts de Build** | [`scripts/`](scripts/) | PowerShell | Pipeline unificado de compilación multiplataforma hacia `dist/`. |
| **Documentación** | [`docs/`](docs/) | Markdown / HTML | Especificación del protocolo ([`spec_protocol.md`](docs/specs/spec_protocol.md)) y mockups interactivos Desktop/Android ([`docs/mockups/`](docs/mockups/)). |

```text
CrossRelay/
├── core/                  # Motor P2P en Go (cmd/, internal/, mobile/)
├── desktop/               # Cliente Desktop en C# / Avalonia UI
├── android/               # Cliente Android en Kotlin / Jetpack Compose
├── scripts/               # Pipeline de compilación unificado
│   ├── build.ps1          # Orquestador principal (full | win | linux | android)
│   ├── build-win.ps1      # Acceso directo para Windows (win-x64)
│   ├── build-linux.ps1    # Acceso directo para Linux (linux-x64)
│   └── build-android.ps1  # Acceso directo para Android (AAR + APK Release)
├── docs/                  # Especificación de protocolo y mockups UI
└── dist/                  # Artefactos compilados listos para distribuir
```

---

## ✔ Requisitos

- **General**: [Git](https://git-scm.com/) y [Go](https://go.dev/) `1.24+`.
- **Para Desktop (`win` / `linux`)**: [.NET SDK](https://dotnet.microsoft.com/) `9.0+`.
- **Para Android (`android`)**: Android Studio (incluye JBR), Android SDK (`API 35+`), **NDK (Side by side)** y `gomobile` (el script lo instala automáticamente si falta).

---

## ⚡ Inicio Rápido

### 1. Clona el repositorio
```sh
git clone https://github.com/Edro08/CrossRelay.git
```

Entra a la carpeta del proyecto:
```sh
cd CrossRelay
```

### 2. Compila con los scripts de `scripts/`

Todos los scripts generan sus artefactos finales dentro de `dist/`:

- **Windows (`dist/win-x64/`)**:
  ```powershell
  .\scripts\build-win.ps1
  ```
  Si Windows bloquea la ejecución de scripts:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\scripts\build-win.ps1
  ```
- **Linux (`dist/linux-x64/`)**:
  ```powershell
  .\scripts\build-linux.ps1
  ```
- **Android (`dist/android/CrossRelay-dev-release.apk`)** *(compila el AAR Go Mobile + APK Release firmada con certificado dev)*:
  ```powershell
  .\scripts\build-android.ps1
  ```
- **Pipeline completo (Windows + Linux + Android)**:
  ```powershell
  .\scripts\build.ps1
  ```

### Opciones de `build.ps1` y `build-android.ps1`

| Flag | Descripción | Ejemplo |
| :--- | :--- | :--- |
| `-Target` | Plataforma a compilar: `full` *(por defecto)*, `win`, `linux` o `android`. | `.\scripts\build.ps1 -Target win` |
| `-SkipAndroidCore` | Omite recompilar `crossrelay.aar` si ya existe y compila solo la APK. | `.\scripts\build-android.ps1 -SkipAndroidCore` |
| `-AndroidTargets` | Arquitecturas NDK para `gomobile` *(por defecto `android/arm64,android/amd64`)*. | `.\scripts\build.ps1 -Target android -AndroidTargets android/arm64` |

### 3. Artefactos generados en `dist/`

| Plataforma | Salida | Contenido |
| :--- | :--- | :--- |
| **Windows** | `dist/win-x64/` | `crossrelay-core.exe` + cliente Avalonia self-contained |
| **Linux** | `dist/linux-x64/` | `crossrelay-core` + cliente Avalonia self-contained |
| **Android** | `dist/android/CrossRelay-dev-release.apk` | APK Release con R8, `libgojni.so` embebido y firma dev |

---

## 🌐 Red y Protocolo

- **Descubrimiento LAN**: mDNS (`_crossrelay._tcp.local.`) y fallback UDP.
- **Puertos por defecto**: `4242` (TCP/TLS 1.3 P2P, configurable a rango o fijo) y `4243` (UDP descubrimiento).
- **Especificación completa**: consulta [`docs/specs/spec_protocol.md`](docs/specs/spec_protocol.md).
