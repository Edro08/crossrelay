# Historial de CrossRelay

## 2026-11-13 — Rediseño UI v2 (Full-Height + Pill Fade), Pausa/Cancelación Real y Consolidación en `docs/`
- **Cambios en UI y Protocolo**:
  - Implementada pausa/reanudación/cancelación real de envíos salientes y cancelación de recepción entrante en Go Core (`/local/v1/transfer/pause`, `/local/v1/transfer/cancel`, `/local/v1/receive/cancel`, `/api/v1/session/{sessionId}/cancel`) y Avalonia Desktop.
  - Promovido el mockup v2 como oficial con vistas `Dispositivos`, `Historial` y `Ajustes` a altura completa hasta la barra inferior, acciones principales en cabecera y botones primarios estilo pill con degradado violeta→oscuro (`.btn-primary-fade`).
  - Consolidada toda la documentación técnica y el mockup oficial en `docs/` (`docs/spec_protocol.md` y `docs/ui_mockup.html`), eliminando `agents/specs/` y actualizando `AGENTS.md` y `agents/context/`.

## 2026-11-09 — Sincronización Bidireccional de Vinculación, Transferencia de Datos y Estabilidad de Alias
- **Solicitud del Usuario**:
  - Al editar el nombre o alias en Ajustes / Ficha de dispositivo, se restablece al nombre base de la máquina y no se propaga al otro cliente.
  - No transfiere datos.
  - En el otro cliente (receptor del PIN) no se mira vinculado, solo en el cliente que digitó el PIN.
- **Diagnóstico y Causa Raíz**:
  1. **Reinicio de Alias en UI por balizas UDP**: Cada 3 segundos llegaba una baliza de radar provocando que `TrustedPeersViewModel` ejecutara `LoadPeersAsync()`, limpiando la lista y reasignando `SelectedPeerItem`, lo que sobreescribía `EditedAlias` mientras el usuario escribía.
  2. **Falta de sincronización de Ajustes con Go Core**: Cambiar el nombre en la vista de Ajustes guardaba solo en el JSON de C#, sin notificar a Go Core. El motor Go seguía emitiendo el hostname original en balizas UDP y pings.
  3. **Receptor sin notificación de vinculación cruzada**: Al ingresar el PIN en la máquina A, el servidor Go de la máquina B verificaba el PIN y guardaba el par, pero nunca emitía un evento SSE `peer_paired` para que la UI de la máquina B supiera que acababa de ser vinculada.
  4. **Fallo en transferencia por SenderID genérico y cancelación de contexto**:
     - `send.Service` tenía codificado `SenderID: "local-device"`, provocando que el receptor rechazara la transferencia (`ErrPeerNotPaired`) al no coincidir con el ID del par vinculado.
     - `handleLocalSend` ejecutaba la transferencia usando `r.Context()`, el cual se cancelaba inmediatamente al responder el HTTP Status 202.
     - Puertos por defecto (`4242`) no contaban con fallback en peticiones REST cuando se omitían en payloads.
- **Correcciones Implementadas**:
  - **`desktop/ViewModels/TrustedPeersViewModel.cs`**:
    - Las balizas UDP ahora actualizan el estado `IsOnline` e IP de los nodos existentes sin limpiar ni recargar la lista de pares de confianza.
    - Se aisló `EditedAlias` mediante `_lastSelectedPeerId` para que solo se cargue al cambiar deliberadamente de dispositivo seleccionado.
  - **Sincronización en Go Core**:
    - Se agregaron endpoints `/local/v1/settings` (GET y POST) en `control/handler.go` y actualización dinámica de balizas en `discovery/udp.go` (`UpdateLocalPeer`).
    - Al guardar ajustes en C#, se notifica inmediatamente a Go Core vía `CoreProcessClient.SaveSettingsAsync`.
  - **Evento `peer_paired` y sincronización cruzada**:
    - Se agregó el evento `peer_paired` en `ports.EventSink`, `Broker` y `CoreProcessClient`. Tanto la máquina iniciadora como la receptora reciben el evento y actualizan su interfaz al instante.
  - **Transferencia de datos**:
    - Se inyectó `settingsStore` en `send.Service` para enviar el `SenderID` y `SenderName` reales del dispositivo.
    - `handleLocalSend` ahora ejecuta la transferencia bajo un contexto desacoplado (`context.Background()`) y reporta errores al broker.
    - Se aplicó fallback seguro de puerto (`constants.DefaultPort`) en todas las llamadas de `rest.Client`.
  - **Compilación Multiplataforma**:
    - Pipeline ejecutado con éxito para Windows (`dist/win-x64/`) y Linux (`dist/linux-x64/`).

## 2026-11-09 — Corrección de Emparejamiento por PIN (Persistencia de DeviceID y Validación Dinámica)
- **Solicitud del Usuario**:
  - Al ingresar el código PIN de 6 dígitos mostrado en una de las máquinas (Windows / Arch Linux), el proceso fallaba con "Digito los pines y nel no conecta".
- **Diagnóstico y Causa Raíz**:
  1. **Generación efímera de `DeviceID` en `json_store.go`**:
     `FileSettingsStore.Get(ctx)` generaba dinámicamente un `DeviceID = "device-" + time.Now().Format("20060102150405")` en cada invocación cuando no existía previamente `settings.json` persistido en disco. Al ejecutar `handleLocalPairInit`, el nodo iniciador se presentaba con un ID (ej. `device-...001`), pero al invocar `handleLocalPairVerify` segundos más tarde, generaba un nuevo ID (ej. `device-...015`).
  2. **Validación estricta de `RequesterID` en `pin.go`**:
     El receptor verificaba `pm.activePin.RequesterID != requesterID`. Al cambiar el identificador del solicitante entre la solicitud y la confirmación, rechazaba la verificación con `ErrPinExpired` (HTTP 408/401).
  3. **Almacenamiento del par de confianza en `server.go`**:
     El receptor guardaba un modelo parcial con únicamente el ID del solicitante en lugar de reconstruir los metadatos completos (`Name`, `OS`, `Fingerprint`) presentes en el `PinChallenge` original.
- **Correcciones Implementadas**:
  - **`core/internal/relay/infrastructure/storage/json_store.go`**:
    Se añadió caché en memoria y persistencia inmediata del archivo `settings.json` en disco ante la primera consulta, asegurando que el identificador del dispositivo permanezca inmutable entre llamadas.
  - **`core/internal/relay/infrastructure/security/pin.go`**:
    Se actualizó `VerifyPin` para retornar el objeto `*model.PinChallenge` verificado, validando el PIN de 6 dígitos con límite de intentos y enfriamiento de seguridad.
  - **`core/internal/relay/infrastructure/handler/http/server.go`**:
    `handlePairVerify` ahora consume el reto validado por `pinMgr.VerifyPin` y registra al par en el almacén de confianza con sus datos completos (`ID`, `Name`, `OS`, `Fingerprint`).
  - **Compilación y Pruebas**:
    Pruebas unitarias de `PinManager` añadidas y validadas con éxito. Binarios re-generados en `dist/win-x64/` y `dist/linux-x64/`.

## 2026-11-09 — Core en Go (Zero-Dependencies), Arquitectura Sidecar y Pipeline Multiplataforma PowerShell
- **Solicitud del Usuario**:
  - Migrar el motor central (`core`) a Go bajo arquitectura limpia inspirada en `go-template` con 0 dependencias externas (100% standard library).
  - Adoptar arquitectura Sidecar: la UI Avalonia Desktop (`C#/.NET 9`) orquesta y consume el proceso local `crossrelay-core` vía HTTP/SSE (`https://127.0.0.1:4242`).
  - Aplanar la estructura de `desktop/` eliminando anidamientos redundantes y remover el antiguo `CrossRelay.Core` en C#.
  - Unificar scripts de build exclusivamente en PowerShell (`scripts/build.ps1`, `scripts/build-win.ps1`, `scripts/build-linux.ps1`).
  - Documentar `readme.md` con estilo técnico similar a Vaultk y Structa.
  - Resolver el problema de arranque de la aplicación de Windows en `dist/win-x64/` y eliminar la ventana de shell/consola auxiliar.
- **Diagnóstico y Corrección**:
  1. **Arranque y Librerías Nativas**: Al publicar con `PublishSingleFile=true`, el runtime fallaba al no resolver los binarios nativos de SkiaSharp. Se corrigió publicando con `--self-contained`, `-p:PublishSingleFile=false` y `-p:PublishAot=false`.
  2. **Eliminación de la Ventana de Consola/Terminal**: `CrossRelay.Desktop.csproj` tenía `<OutputType>Exe</OutputType>`, lo que marcaba el binario con el subsistema CUI (consola) y provocaba que Windows Terminal abriera una ventana de shell negra junto a la UI. Se cambió a `<OutputType>WinExe</OutputType>` (subsistema GUI nativo) y se reforzó `WindowStyle = ProcessWindowStyle.Hidden` en el spawn del demonio Go.
  3. **Límites de Tamaño de Ventana y Robustez de Layout (Anti-Colisión)**:
     - Se ajustaron las dimensiones mínimas de la ventana en `MainWindow.axaml` a `MinWidth="1000"` y `MinHeight="640"` (con dimensiones por defecto `1120x740`).
     - Se reestructuró la cabecera de la tarjeta en `SendView.axaml`: la píldora de estado (`• Sin vínculo`) ahora acompaña a `Destino:` en la segunda línea en lugar de encadenarse en una sola fila horizontal, liberando más de 120px de espacio.
     - Se dotó al área de arrastre (dropzone) de una grilla `Auto,*,Auto` con `TextTrimming="CharacterEllipsis"` para blindar el botón `Explorar disco...` contra solapamientos tipográficos.
  4. **Activación del Radar UDP y Soporte Multi-Interfaz en Go Core**:
     - **Causa raíz de la falta de descubrimiento**: En `core/cmd/main.go`, el contenedor inicializaba el servicio `container.Discovery` pero nunca llamaba a `container.Discovery.Start(ctx, myPeer)`. Ni Windows ni Linux estaban emitiendo ni escuchando balizas en el puerto `4243`.
     - Se activó el radar UDP en `cmd/main.go` con detección de hostname del sistema operativo.
     - Se refactorizó `udp.go` para emitir balizas dirigidas por cada interfaz de red activa (`net.Interfaces()`), a nivel de subred (`192.168.x.255`, `169.254.x.255`) y a la dirección multicast `239.255.255.250`.
     - Se agregó endpoint `/local/v1/peers/probe` y campo en la UI para conexión directa por IP ante posibles routers con aislamiento de clientes.

## 2026-11-08 — Unificación Cromática Pure OLED Black & Electric Indigo / Obsidian (Estándar Structa & Vaultk)
- **Solicitud del Usuario**:
  - Integrar el nuevo icono oficial de la aplicación y alinear toda la paleta de colores de **CrossRelay** con el sistema cromático de **Structa** y **Vaultk** (basado en el fondo negro OLED puro `#000000` del icono).
- **Auditoría de `Structa` (`Color.kt`) y `Vaultk` (`Color.kt`) y Cambios Aplicados**:
  1. **Fondo Pure OLED Black (`#000000`) e Integración del Icono**:
     - Actualizados `MainWindow.axaml`, `App.axaml` y `agents/specs/ui_mockup.html` para usar `#000000` (`PureBlack` / `DarkBackground`) en el lienzo principal, barra lateral y barra de estado inferior, haciendo que el fondo `#000000` del nuevo icono `desktop/Assets/crossrelay-logo.png` se funda limpiamente sin bordes artificiales.
  2. **Superficies Obsidian y Campos (`#0E0F12`, `#131316`, `#18191E`, `#0D0D10`)**:
     - Reemplazados los fondos azul-marino (`#090A10`, `#0C0D14`, `#08090F`) por las superficies neutras Obsidian de Structa y Vaultk: `#0E0F12` (`DarkSurface`) para tarjetas principales, `#131316` (`VaultObsidian`) para filas y subcontenedores, `#18191E` (`DarkSurfaceRaised`) para botones secundarios, y `#0D0D10` con borde `#23222B` para campos de texto.
  3. **Acentos de Marca Electric Indigo (`#582BF5`) y Lavender (`#C185FC` / `#818CF8`)**:
     - Eliminado el esquema disperso de colores arcoíris (celestes/cyan/amarillos) en iconos y píldoras, unificando las cajas de icono con `VaultTileBackground` (`#1E1738`), `VaultTileBorder` (`#332763`) y tinte `VaultLavender` (`#C185FC`), reservando `#10B981` (`StatusGreen`) y `#EF4444` (`StatusRed`) exclusivamente para estados semánticos.

## 2026-11-08 — Ventana Integrada sin Barra Superior Nativa, Nuevo Icono `.ico` Oficial y Eliminación del Badge de Seguridad Inferior
- **Solicitud del Usuario**:
  - Ocultar la barra superior nativa de Windows (reemplazándola por controles integrados `— □ ✕` en la esquina superior derecha y el icono oficial `>>` de CrossRelay) y eliminar la tarjeta inferior `Transferencia segura y directa` del sidebar.
- **Detalle de la Implementación**:
  1. **Ventana sin Barra Nativa (`MainWindow.axaml` y `MainWindow.axaml.cs`)**:
     - Configurado `WindowDecorations="BorderOnly"`, `ExtendClientAreaToDecorationsHint="True"` y `ExtendClientAreaTitleBarHeightHint="-1"` para conservar bordes redondeados y sombra de Windows 11 eliminando por completo la barra de título nativa redundante.
     - Añadida franja superior integrada de `24px` con soporte de arrastre (`BeginMoveDrag`), doble clic para maximizar/restaurar y botones minimalistas (`—`, `□`, `✕`) estilizados con `win-ctrl-btn` en `App.axaml`.
  2. **Icono Oficial de la Aplicación (`Assets/avalonia-logo.ico` y `CrossRelay.Desktop.csproj`)**:
     - Generado archivo `.ico` de alta resolución (`256x256`) con el isotipo violeta neón `>>` de CrossRelay (`#6366F1 -> #A855F7`) y configurado `<ApplicationIcon>` en el proyecto.
  3. **Eliminación de la Tarjeta `Transferencia segura y directa`**:
     - Removida la tarjeta verde inferior y su línea divisoria tanto de `MainWindow.axaml` como de la especificación `agents/specs/ui_mockup.html`.

## 2026-11-08 — Sincronización Matemática de Coordenadas SVG (`Viewbox 24x24`) y Fidelidad Pixel-Perfect al 100%
- **Solicitud del Usuario**:
  - Corregir todos los iconos y detalles visuales que no hacían match 1:1 con el mockup HTML (`agents/specs/ui_mockup.html`).
- **Causa Raíz y Solución Aplicada**:
  1. **Sistema de Coordenadas SVG Exacto (`<Viewbox>` + `<Canvas Width="24" Height="24">` + `Stretch="None"`)**:
     - En Avalonia, `Stretch="Uniform"` sobre un `<Path>` escalaba cada icono según su caja delimitadora propia en lugar del lienzo de `24x24` de Lucide SVG (haciendo que chevrón `⌵`, cruz `✕`, pausa `⏸` o el icono de Windows se deformaran o descentraran).
     - Se actualizaron `App.axaml`, `MainWindow.axaml`, `SendView.axaml`, `ReceiveView.axaml`, `TrustedPeersView.axaml`, `HistoryView.axaml` y `SettingsView.axaml` para envolver cada icono dentro de `<Viewbox><Canvas Width="24" Height="24"><Path Stretch="None" .../></Canvas></Viewbox>`, logrando una equivalencia matemática 1:1 con `<svg viewBox="0 0 24 24">`.
  2. **Iconos Faltantes y Geometrías Corregidas (`App.axaml`)**:
     - Añadido `IconClock` (anteriormente ausente, lo que hacía invisible el reloj en la píldora `⏱ Completado` de `SendView`).
     - Añadido `IconTrashSimple` para el botón de vaciar historial en `HistoryView`.
     - Corregido `IconWindows` cerrando los 4 cuadrantes (`Z`) para renderizar los 4 paneles de Windows 11 sin recortes.
     - Corregidos `IconPause` e `IconBolt` para renderizarse con trazo (`stroke-icon`) en lugar de relleno sólido, y `IconPhone` con el punto inferior de pantalla.
  3. **Detalles Estructurales de las 5 Pantallas y Modal**:
     - `MainWindow.axaml`: Bordes divisores de `1px` (`#151624`) en la barra lateral y sobre el badge de seguridad inferior; fondo principal `#090A10`.
     - `SendView.axaml`: Línea divisoria inferior en la cabecera de `Lote de Transferencia`, píldora `• Completado` centrada a la derecha del bloque título/subtítulo, borde punteado real (`StrokeDashArray="4,3"`) en la zona drag-and-drop, barra de progreso con degradado `#6366F1 -> #A855F7`, icono morado para el ítem `En cola`, y divisores en el modal `Seleccionar Dispositivo de Destino`.
     - `ReceiveView.axaml`: Eliminado `MinHeight="420"` artificial para respetar el alto natural (`Padding="48"`, `VerticalAlignment="Top"`) del mockup.
     - `TrustedPeersView.axaml`: Divisor inferior en cabecera de ficha, radios azul/gris exactos en `Política de Aceptación`, y ajuste de proporciones para que todas las rutas y textos se muestren completos sin truncamiento.
     - `HistoryView.axaml`: Subtítulos enriquecidos con `<Run>` multicolor (`Enviado a:` en gris, nodo en blanco semibold, hora en fuente monoespaciada).
     - `SettingsView.axaml`: Ajuste tipográfico y de padding en tarjetas mDNS y Trazabilidad para mostrar las descripciones completas en una sola línea.

## 2026-11-07 — Perfeccionamiento Visual al 100% de Fidelidad con el Mockup HTML Oficial
- **Solicitud del Usuario**:
  - Lograr fidelidad visual absoluta al 100% con respecto al mockup HTML (`ui_mockup.html`) y las capturas de referencia, eliminando cualquier discrepancia de iconos, proporciones, espacios o truncamientos.
- **Auditoría y Correcciones Realizadas**:
  1. **Iconografía Vectorial Lucide Nativa**:
     - Migración de `PathIcon` (que rellenaba trazados abiertos produciendo formas deformadas) a `Path` con estilo `stroke-icon` (`StrokeThickness="1.8"`, `StrokeJoin="Round"`, `StrokeLineCap="Round"`, `Fill="Transparent"`), permitiendo que todos los iconos de trazo Lucide (doble chevrón `>>`, avión de papel `Enviar`, bandeja `Recibir`, monitor `Dispositivos`, reloj `Historial`, engranaje `Ajustes`, escudo `Seguridad`, nube, cámara de video, flechas `↑`/`↓`, papelera, etc.) se rendericen con máxima nitidez idéntica a SVG en el navegador.
  2. **Pantalla Enviar (`SendView.axaml`)**:
     - Reducción y armonización de espaciados verticales para que los 4 archivos de la cola (`Season 01 Episodio 09`, `10`, `11` y `12`) encajen perfectamente en pantalla sin recortes.
     - Botón de limpieza (`Limpiar ⌵`) con icono de papelera y flyout para completados, errores, inactivos y vaciado total.
     - Botón `Pausar` con espaciado correcto respecto al icono de pausa.
     - Donut circular de 80x80 al 100% con tipografía Consolas nítida y badges `⚡ 21.9 MB/s` y `⏱ Completado`.
  3. **Modal de Cambio de Dispositivo (`ModalCambiar`)**:
     - Ventana modal superpuesta con desenfoque de fondo oscuro, buscador con lupa, tarjetas de estado (`edro` activo con badge `Actual`, `linux-mint` y `Galaxy S24` con botón `Elegir`) y pie de diálogo.
  4. **Pantalla Dispositivos (`TrustedPeersView.axaml`)**:
     - Ajuste de columna izquierda a 210px para brindar suficiente espacio a la columna derecha.
     - Cuadrícula de 4 métricas (`DATOS TOTALES 34.8 GB`, `ARCHIVOS 142`, `VELOCIDAD MEDIA 32.4 MB/s`, `LATENCIA LAN 1.8 ms`) sin solapamiento.
     - Ficha técnica completa con avatar `ED`, alias, carpeta dedicada, tarjetas de política de aceptación (`Descarga Automática` vs `Confirmar Antes`) y acciones (`Bloquear`, `Desvincular`, `Guardar Cambios`).
  5. **Pantalla Historial (`HistoryView.axaml`)**:
     - Píldoras de filtro (`Todos (12)`, `Enviados (8)`, `Recibidos (4)`) y botón de purga con icono de papelera.
     - 4 filas de auditoría con velocidades en verde esmeralda (`#34D399`), píldoras `✓ Completado` y botones `Abrir`.
  6. **Pantalla Ajustes (`SettingsView.axaml`)**:
     - 4 tarjetas de selección de ancho de banda (`Sin Límite` con borde púrpura neón, `100 MB/s`, `50 MB/s`, `10 MB/s`).
     - Entradas de identidad y carpeta de descarga, más tarjetas de dos columnas para mDNS y Trazabilidad con `TextWrapping="Wrap"`.
  7. **Barra de Estado Inferior (`MainWindow.axaml`)**:
     - Conexión exacta de telemetría: punto verde `🟢`, `En línea`, `EDRO08 (:4242)`, `TLS 1.3 (ECDSA)`, velocidades `↑ 0.0 MB/s`, `↓ 0.0 MB/s` y píldora `Sin límite`.
- **Verificación**:
  - Compilación limpia con 0 errores (`dotnet build`).
  - Capturas generadas y verificadas visualmente mediante inspección de imágenes de alta resolución.

## 2026-11-07 — Implementación Inicial de la UI Relay en Avalonia Desktop
- **Solicitud del Usuario**:
  - Implementar todos los cambios visuales y estructurales del mockup en la aplicación de escritorio Avalonia.
- **Detalle de la Implementación**:
  - Configuración inicial de vistas `SendView`, `ReceiveView`, `TrustedPeersView`, `HistoryView`, `SettingsView`.

## 2026-11-06 — Actualización de Marca: Nombre Oficial 'CrossRelay' en el Mockup
- **Solicitud del Usuario**:
  - Corregir el nombre de la cabecera en el sidebar y el título del mockup para reflejar la marca oficial del proyecto: **CrossRelay**.
- **Detalle de la Implementación**:
  - `relay_mockup.html` & `agents/specs/ui_mockup.html`:
    - Actualizado el logotipo y título de la barra lateral a **CrossRelay** (`Move files. Keep control.`), manteniendo el isotipo de doble chevrón violeta neón.

## 2026-11-06 — Adaptación Integral del Mockup a Todas las Pantallas (Dispositivos, Historial y Ajustes)
- **Solicitud del Usuario**:
  - Adaptar y transformar en el mockup las pantallas restantes (*Dispositivos*, *Historial*, *Ajustes*) a la estética obsidian y violeta/neón de Relay, dejando *Recibir* intacta para personalización posterior.
