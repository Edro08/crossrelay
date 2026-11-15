# Fases de desarrollo de CrossRelay

## 🟢 Fases completadas
- **Fase 0: Definición Arquitectónica y Estructura Monorepo**
  - [x] Evaluación y selección de stack: Kotlin (Android) + C# Avalonia (Desktop Windows/Linux).
  - [x] Configuración de carpetas de proyecto aisladas (`android/`, `desktop/`, `docs/`, `core/`).
  - [x] Configuración de contexto y estándares `edro-dev`.
  
- **Fase 1: Especificación Formal del Protocolo LAN (v1.0)**
  - [x] Definición de descubrimiento mDNS / UDP (`_crossrelay._tcp.local.`, puerto 4242).
  - [x] Especificación de seguridad: TLS 1.3 estricto, par de claves elípticas y PIN digitado de 6 dígitos.
  - [x] Definición de contratos JSON de sesión y endpoints REST internos.
  - [x] Estrategia de streaming continuo sin compresión redundante, buffers de 1MB y limitador de velocidad.
  - [x] Mockup visual interactivo completado y archivado en `docs/mockups/ui_mockup.html`.

- **Fase 2: Implementación Core y Desktop (Go + C# Avalonia)**
  - **2.1 Motor Hexagonal (`CrossRelay.Core`)**
    - [x] **Modelos de Dominio y Puertos Hexagonales**: `PeerDevice`, `TrustedPeer`, `TelemetryStats`, `TransferItem`, `TransferBatch`, `LocalNodeIdentity`, enums y puertos de entrada/salida.
    - [x] **Criptografía TLS 1.3**: Generador de certificados efímeros ECDSA P-256 en memoria (`CertificateManager`), validador anti-MITM por huella digital y gestor de desafíos de PIN con rate-limiting (`PinPairingService`).
    - [x] **Descubrimiento mDNS**: Adaptador `MdnsDiscoveryService` sobre `_crossrelay._tcp.local.` con TXT records (`id`, `name`, `os`, `ver`, `fp`) y eventos de presencia.
    - [x] **Motor de Streaming Masivo (400 MB/s)**: `BatchStreamEngine` continuo para 10,000+ archivos sin round-trips HTTP, bypass de compresión multimedia, contrapresión de disco y `TokenBucketRateLimiter` (Sin límite, 100, 50, 10 MB/s).
    - [x] **Repositorios de Persistencia Local**: `FileTrustedPeersRepository`, `FileSettingsRepository` y `FileBlacklistRepository` con almacenamiento seguro en `%AppData%/CrossRelay`.
    - [x] **Capa de Transporte y Fachada**: `CrossRelayHttpServer` (TLS 1.3, NoDelay, buffers 1MB), `CrossRelayHttpClient` y la composición raíz `CrossRelayEngine`.
    - [x] Compilación y verificación exitosa con 0 errores y 0 advertencias.
  - **2.2 Capa de Presentación Desktop (Avalonia UI + MVVM)**
    - [x] **Shell con Temas Fluent Dark (Obsidian)**: Fondo `#0F1117`, tarjetas `#1A1D27`, acentos `#6366F1` y distintivo de marca "CR".
    - [x] **Barra Lateral Plegable (`SplitView`)**: Modo colapsado (56px con sólo iconos y tooltips al pasar el cursor) vs Modo expandido (210px con iconos + texto), con botones toggle interactivos.
    - [x] **Barra Inferior de Estado de Red (`BottomStatusBar`)**: Indicador reactivo 🟢 En línea, nombre del nodo, puerto, TLS 1.3, telemetría de velocidad en vivo (↑ / ↓) y modo de limitación de ancho de banda.
    - [x] **Pantalla 1: Enviar (`SendView`)**: Radar de dispositivos descubiertos en tiempo real, dropzone para arrastrar lotes y barra de progreso continua.
    - [x] **Pantalla 2: Recibir (`ReceiveView`)**: Estación de escucha activa, apertura de carpeta destino nativa y visor de descargas en curso.
    - [x] **Pantalla 3: Vínculos (`TrustedPeersView`)**: Panel maestro-detalle con edición de alias, políticas `AutoAccept` / `AskConfirmation`, huella TLS 1.3 y estadísticas acumuladas.
    - [x] **Pantalla 4: Historial (`HistoryView`)**: Registro auditado de transferencias con filtros por píldora (`Todos`, `Enviados`, `Recibidos`).
    - [x] **Pantalla 5: Ajustes (`SettingsView`)**: Configuración de nombre, puerto, selector de límite Token Bucket (Sin límite, 100, 50, 10 MB/s) y gestión de lista negra.
    - [x] **Diálogo Modal de Emparejamiento por PIN**: Overlay con código de 6 dígitos en fuente monoespaciada gigante y temporizador de 60 segundos.
  - **2.3 Integración Nativa con el Sistema Operativo**
    - [x] Soporte de arrastre y soltado de archivos (*Drag & Drop*) del SO directo sobre la dropzone con API moderna `DataFormat.File` y `TryGetFiles()`.
    - [x] Minimizar a la bandeja del sistema (*System Tray*) al cerrar la ventana (`Closing` cancelado y re-apertura desde menú de bandeja).
    - [x] Selector nativo de archivos y carpetas vía `StorageProvider` (`OpenFilePickerAsync` / `OpenFolderPickerAsync`).
    - [x] Bandeja del sistema (`TrayIcon`) con menú nativo ("Abrir CrossRelay", "Salir").
  - **2.4 Optimización y Empaquetado Native AOT**
    - [x] Reemplazo de reflexión dinámica por tipado directo seguro en `ViewLocator.cs`.
    - [x] Configuración de perfil Release Native AOT (`PublishAot`, `StripSymbols`) en `CrossRelay.Desktop.csproj`.
    - [x] Compilación exitosa en Debug y Release con **0 Advertencias y 0 Errores**.
  - **2.5 Trazabilidad Asíncrona, Dependencias Seguras y Descubrimiento mDNS**
    - [x] Optimización extrema del pipeline de streaming: eliminación de cuellos de botella en TCP, I/O y Avalonia UI Dispatcher.
    - [x] Modal global de aceptación/rechazo de transferencias entrantes con protocolo `Expect: 100-continue`.
    - [x] Navegación reactiva automática a la pestaña `Recibir` al aceptar transferencias desde cualquier pantalla.
    - [x] Integración de telemetría de recepción en vivo y registro de descargas en el Historial auditado.
    - [x] Persistencia permanente de certificados TLS 1.3 (`node_cert.pfx`) en disco para estabilidad de huellas digitales anti-MITM.
    - [x] Actualización reactiva de huellas e IPs de nodos descubiertos en caliente en `SendViewModel` y `MdnsDiscoveryService`.
    - [x] Corrección crítica de streaming binario: eliminación del búfer overread de `StreamReader` en `CrossRelayHttpServer` mediante `ReadHttpHeadersAsync` y `PrefixStream`.
    - [x] Confirmación bidireccional HTTP 200 OK en `CrossRelayHttpClient` tras verificación de lotes.
    - [x] Persistencia bidireccional total de `settings.json`, sincronizando `SettingsView`, `ReceiveView`, barra de estado y el motor `CrossRelayEngine` en vivo.
  - **2.6 Rediseño Integral de UI (Vaultk Pitch-Black OLED), Soporte de Carpetas, Cola Granular y Vinculación PIN**
    - [x] Tema Pitch-Black OLED completo inspirado en Vaultk (`#000000`, `#050608`, `#0A0C10`, bordes `#161922`, cian neón `#00F0FF`).
    - [x] Eliminada caja desproporcionada en `SendView`; layout balanceado en 2 columnas con tarjetas compactas de pares.
    - [x] Preservación intacta del nombre de carpetas raíz y subdirectorios recursivos tanto en el emisor como en el receptor.
    - [x] Botones individuales de eliminación `[✕]` por archivo en la lista de preparación y actualización dinámica de totales.
    - [x] Monitor de lote detallado en el espacio inferior mostrando progreso global y tabla en vivo de cada archivo transferido.
    - [x] Sistema de vinculación por PIN de extremo a extremo completado con endpoints `/api/v1/pair/init`, `/api/v1/pair/verify`, modales de entrada y guardado en confianza.
    - [x] Selector nativo de carpetas de descarga (`StorageProvider.OpenFolderPickerAsync`) integrado en Ajustes, Recepción y Vínculos.
    - [x] Migración de paquete mDNS a `Makaretu.Dns.Multicast.New` (v0.38.0), eliminando dependencias obsoletas y vulnerabilidades transitivas.
    - [x] Eliminación total de datos mock / dummy en `SendViewModel`, `ReceiveViewModel`, `TrustedPeersViewModel` e `HistoryViewModel`.
    - [x] Diseño e implementación de estados vacíos elegantes y reactivos en todas las vistas de Avalonia.
    - [x] Implementación de `CrossRelayLogger` no bloqueante mediante canales de memoria (`System.Threading.Channels`).
    - [x] Corrección de selección de adaptador de red mDNS (exclusión de Hyper-V `vEthernet`, WSL y VPNs; enlace a Wi-Fi física `192.168.0.x`).
    - [x] Anuncio explícito de direcciones IPv4 físicas en registros DNS-SD ARecord.
    - [x] Búsqueda periódica mDNS (5s) para tolerancia a caídas de paquetes UDP en Wi-Fi y botón de refresco en UI.
    - [x] Binario de release para Linux regenerado y verificado en `dist/linux-x64/`.
    - [x] Instalador XDG para Linux (`install.sh` / `uninstall.sh`), instancia única (`SingleInstanceGuard`), sincronización automática de `downloadDirectory` al Core, expiración limpia de confirmaciones y opción `BringToFrontOnRequest` en Ajustes.

- **Fase 3: Implementación Android (`gomobile` + Kotlin + Jetpack Compose)**
  - **3.1 Motor Nativo (`gomobile` `.aar` / `.so` sobre `core/`) y Servicios Android**
    - [x] Wrapper Go móvil (`core/mobile/mobile.go`) + build pipeline `gomobile bind` (`arm64-v8a`, `x86_64`) unificado en `scripts/build.ps1` / `scripts/build-android.ps1` reutilizando el 100% de `core/` (TLS 1.3 ECDSA P-256, radar UDP `:4243`, `/api/v1/*`, PIN pairing, `TokenBucket`, `DuplicatePolicy`, `PortMode` y streaming SHA-256).
    - [x] `ForegroundService` (`CrossRelayService`, `dataSync`) con `MulticastLock`, `WifiLock` y `WakeLock` parcial para mantener activo el servidor P2P, el radar UDP `:4243` y transferencias en segundo plano.
    - [x] Notificación persistente en tiempo real con progreso, velocidad y acciones rápidas (**Pausar/Reanudar**, **Cancelar**).
  - **3.2 Almacenamiento con Control Total (sin puente SAF) y Share Sheet**
    - [x] Permiso `MANAGE_EXTERNAL_STORAGE` y `POST_NOTIFICATIONS` con tarjeta de gestión en Ajustes (`SettingsScreen`) y flujo hacia Ajustes de Android estilo Structa/Vaultk.
    - [x] Soporte completo de carpeta global, escritura segura `.part`, verificación SHA-256 y `DuplicatePolicy` (`rename`, `overwrite`, `skip`).
    - [x] Indexación automática en `MediaStore` (`MediaScannerConnection`) al finalizar recepción de archivos multimedia.
    - [x] Integración con **Android Share Sheet** (`Intent.ACTION_SEND` / `ACTION_SEND_MULTIPLE`) y selector de archivos/carpetas directo en almacenamiento compartido.
  - **3.3 UI en Jetpack Compose (Paridad con `docs/mockups/ui_mockup_android.html`, estilo Structa + Vaultk)**
    - [x] Tema Pitch-Black OLED (`#000000` / `#0E0F12` / `#12141A`, acentos `#7C3AED` / `#C185FC`), barra de navegación inferior Material 3, dropdowns oscuros y hojas modales (`ModalBottomSheet`).
    - [x] **Enviar**: selector de peer, conexión manual `IP:puerto`, cola granular de archivos/carpetas con filtro **Limpiar** y barra de transferencia anclada al fondo.
    - [x] **Recibir**: estación de escucha activa, progreso/velocidad en vivo, listado por archivo del lote entrante y controles de cancelación.
    - [x] **Dispositivos**: descubiertos en LAN con **Solicitar conexión**, vínculos de confianza, modal de emparejamiento PIN de 6 dígitos, edición de alias y carpeta dedicada por dispositivo, política `AutoAccept`/`AskConfirmation` y bloqueo.
    - [x] **Historial**: registro agrupado por sesión con filtros (`Todos`, `Enviados`, `Recibidos`).
    - [x] **Ajustes**: selector dropdown de velocidad, **Permisos del sistema**, **Identidad y Red** (nombre, visibilidad mDNS, política de puertos, diagnóstico de puertos), **Almacenamiento y Archivos** (carpeta global + duplicados) y **Nodos Bloqueados**.
    - [x] Modales globales de confirmación de transferencia entrante (`AskConfirmation`), solicitud de vinculación PIN y diagnóstico de puertos.
    - [x] Build Release firmada con certificado dev + R8 en `dist/android/CrossRelay-dev-release.apk`.

---

### Fases Posteriores

#### Fase 4: Pruebas de Interoperabilidad Cruzada y Rendimiento
- [ ] **Android ↔ Windows**: emparejamiento PIN bidireccional, transferencia de lotes/carpetas anidadas, verificación SHA-256 y `DuplicatePolicy` (`rename`, `overwrite`, `skip`).
- [ ] **Android ↔ Linux (Arch/Ubuntu)**: validación de descubrimiento UDP `:4243`, diagnóstico de puertos + reglas `firewalld`/`ufw` y conexión manual `IP:puerto`.
- [ ] **Resiliencia y Seguridad**: pausa/cancelación en vivo en ambos sentidos, rechazo a nivel TLS de nodos bloqueados y transferencias sostenidas con pantalla apagada (`ForegroundService`).

#### Fase 5: Descubrimiento Multi-Interfaz y Preferencia Ethernet Directo
- **5.1 Visibilidad de IP Link-Local Ethernet (`169.254.x.x`) en Desktop**
  - [x] Actualizar `ResolveLocalIp()` en `desktop/ViewModels/MainViewModel.cs` para filtrar adaptadores virtuales/VPN/Bluetooth y mostrar IPs `169.254.x.x` de interfaces físicas Ethernet junto a Wi-Fi en la barra de estado.
- **5.2 Multi-ruta y Prioridad Ethernet en `core/` (`UDPDiscovery`)**
  - [x] Mantener múltiples rutas activas por `PeerID` (`peerRoutes`) en `core/internal/relay/infrastructure/discovery/udp.go` y priorizar automáticamente interfaces cableadas Ethernet sobre Wi-Fi y adaptadores virtuales, con failover automático si se desconecta el cable.
  - [x] Pruebas unitarias en `core/internal/relay/infrastructure/discovery/udp_test.go` validando selección de ruta y failover.
- **5.3 Validación en Vivo**
  - [ ] Recompilar binarios (`build.ps1`) y verificar transferencia directa por cable Ethernet Windows ↔ Linux.

---

### Puntos de Mejora (Backlog)
- [x] Cerrar automáticamente el modal global `Solicitud de Vinculación Entrante` (`IsPinDialogOpen = false` en `MainViewModel`) en el equipo receptor cuando se reciba el evento `PeerPairingCompleted` / `peer_paired` tras validar el PIN.
- [x] En el historial del receptor (`handleSessionUpload` / `CrossRelayEngine`), agrupar la transferencia por sesión (`sessionId`) e incluir el `SenderName`, nombre del lote y `TotalBytes` reales en lugar de mostrar `Transferencia • 0 B` y `Recibido de: Peer` por cada archivo.
- [x] Diseñar e implementar la vista en vivo de `Recibir` (`ReceiveView` / `ReceiveViewModel`) para mostrar progreso real, velocidad y archivos entrantes, y filtrar los eventos `TransferProgress` / `TransferCompleted` por dirección (`outgoing` vs `incoming`) para que `SendView` y la barra inferior (`↑` / `↓`) no mezclen métricas del receptor con las del emisor.
- [x] Implementar pool de puertos TCP de respaldo para el servidor P2P TLS 1.3 (`4242`, `4244..4248`, manteniendo fijo el radar UDP `4243`) y para la API de control local (`14242..14250`), incluyendo soporte de `IP:puerto` y escaneo del pool en la conexión manual por IP.
- [x] Conectar de extremo a extremo el modal de confirmación/rechazo (`Confirmar Antes` vía `/local/v1/transfer/respond` y sincronización `/local/v1/trusted/update`) e implementar la Opción B para `Descarga Automática` (Toast flotante en vivo dentro de la app + notificación nativa del SO al estar minimizada/oculta).
