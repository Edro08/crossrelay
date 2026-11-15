# Especificación del Protocolo CrossRelay (v1.0)

## 1. Visión General

CrossRelay es un protocolo de transferencia de datos y archivos punto a punto (P2P) diseñado para redes de área local (LAN / Wi-Fi). Sus principios fundamentales son:
- **Descubrimiento sin configuración (Zero-Config)**: Detección automática de nodos en la misma subred.
- **Seguridad obligatoria (Zero-Trust)**: Cifrado en tránsito punto a punto vía TLS 1.3 con certificados efímeros y verificación de identidad.
- **Rendimiento máximo (High Throughput)**: Streaming asíncrono con control de flujo sin carga completa en memoria (Zero-Copy I/O).
- **Interoperabilidad multiplataforma**: Contratos agnósticos al lenguaje (C# .NET en Desktop, Kotlin en Android).

---

## 2. Descubrimiento de Nodos (Peer Discovery)

### 2.1 mDNS (Multicast DNS) + DNS-SD (RFC 6762 / 6763)
- **Tipo de servicio**: `_crossrelay._tcp.local.`
- **Puerto de servicio estándar**: `4242` (con asignación dinámica si el puerto está en uso).
- **Registros TXT obligatorios**:
  | Clave | Tipo | Descripción | Ejemplo |
  | :--- | :--- | :--- | :--- |
  | `id` | string | UUID v4 único y persistente del nodo | `f47ac10b-58cc-4372-a567-0e02b2c3d479` |
  | `name` | string | Nombre visible del dispositivo en red | `Pixel 9 Pro` / `Edro-Workstation` |
  | `os` | string | Sistema operativo (`android`, `windows`, `linux`, `macos`) | `windows` |
  | `ver` | string | Versión del protocolo CrossRelay | `1` |
  | `fp` | string | Huella digital SHA-256 del certificado TLS activo | `7a8f90b1c2...` |

### 2.2 Fallback: UDP Broadcast
- **Puerto UDP**: `4243`
- **Dirección**: `255.255.255.255`
- **Frecuencia**: Ping cada 5 segundos cuando la pantalla de radar está activa.
- **Payload**: JSON idéntico a los campos del registro TXT de mDNS.

---

## 3. Seguridad y Cifrado

### 3.1 Cifrado en Tránsito (TLS 1.3)
- **Versión mínima y única**: **TLS 1.3** (Cipher suites modernas: `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256`).
- **Certificados**:
  - Certificado X.509 autofirmado generado por cada nodo.
  - Par de claves con curva elíptica moderna (**ECDSA P-256** o **Ed25519**).
  - La clave pública y su huella SHA-256 se anuncian en el descubrimiento mDNS para prevenir ataques de intermediario (Man-In-The-Middle / MITM).

### 3.2 Fase Obligatoria de Emparejamiento Previo con PIN Digitado (Anti-Fatiga de Confirmación)
Estar en la misma subred Wi-Fi/LAN **no concede permiso para transferir archivos directamente**. Para evitar aprobaciones accidentales por simple clic ("dar Sí por error"), el protocolo exige que el usuario **digite activamente un código de 6 dígitos**:

1. **Estado Inicial**: Al descubrir un nuevo nodo por mDNS, se clasifica como `No Emparejado (Unpaired)`. La opción de enviar archivos permanece bloqueada para ese nodo.
2. **Inicio del Desafío (`POST /api/v1/pair/init`)**:
   - El nodo iniciador (ej. móvil) envía una solicitud de vinculación.
   - El nodo receptor (ej. PC) genera en memoria un PIN criptográfico aleatorio de 6 dígitos (ej. `492815`) con vigencia estricta de **60 segundos** y lo muestra en pantalla grande.
3. **Digitación y Verificación (`POST /api/v1/pair/verify`)**:
   - El nodo iniciador muestra una interfaz con 6 casillas numéricas (OTP inputs).
   - El usuario debe leer físicamente la pantalla del receptor y **teclear el PIN de 6 dígitos**.
   - El nodo iniciador envía el PIN introducido hacia el receptor.
   - **Protección contra fuerza bruta**: Se permiten un máximo de **3 intentos**. Al tercer fallo consecutivo, la solicitud se cancela y el nodo queda en lista negra temporal por 5 minutos.
4. **Sellado de Confianza y Token**:
   - Si el PIN coincide, ambos nodos intercambian y guardan localmente el `pairingToken` vinculado a la huella digital (`fp`) del certificado del par.
   - El nodo queda en estado `Emparejado / Confiable (Paired)`.
5. **Modos de Visibilidad y Privacidad**:
   - **Visible para Todos**: Permite recibir solicitudes de emparejamiento con el desafío de PIN.
   - **Solo Emparejados**: Solo responde a transferencias de nodos previamente vinculados; ignora peticiones de desconocidos.
   - **Modo Invisible**: Apaga los anuncios mDNS y no responde solicitudes.

### 3.3 Gestión de Vínculos y Políticas de Confianza (Trusted Peers Manager)
Una vez superado el desafío del PIN, el nodo se registra en el almacén seguro local de dispositivos vinculados. El usuario tiene control total e interactivo para administrar y editar cada vínculo:

1. **Campos Editables por Dispositivo**:
   - **Alias Personalizado**: El usuario puede renombrar el nodo remoto en cualquier momento (ej. de `"SM-S928U"` a `"Mi Teléfono Personal"`).
   - **Modo de Auto-Aceptación (Transfer Policy)**:
     - `AutoAccept` (Descarga Automática): Ideal para dispositivos propios. Los archivos se reciben y guardan silenciosamente en disco sin requerir interacción manual en el receptor, emitiendo solo una notificación al finalizar.
     - `AskConfirmation` (Confirmación Manual): Para dispositivos de terceros o de confianza media. No pide PIN, pero muestra una alerta previa en pantalla: *"¿Aceptar 3 archivos (45 MB) de Juan? [Aceptar] [Rechazar]"*.
   - **Carpeta de Descarga Dedicada**: Cada vínculo puede tener asignada una ruta de descarga específica (ej. `D:\Fotos\Móvil` o `C:\Proyectos\Transferencias`). Si no se define, se usa la carpeta global de la aplicación.
2. **Acciones de Administración**:
   - **Desvincular / Eliminar**: Revoca de inmediato el `pairingToken`. El nodo remoto pierde el acceso y requerirá un nuevo PIN para volver a conectarse.
   - **Bloquear (Blacklist)**: Mueve el nodo a la lista negra (ver sección 3.5).

### 3.4 Telemetría y Estadísticas del Vínculo (Peer Telemetry & Metrics)
Cada dispositivo vinculado acumula métricas de rendimiento y uso localmente para auditoría, calidad de enlace y visualización en la UI:

1. **Métricas de Conexión en Tiempo Real**:
   - **Latencia de Enlace (RTT)**: Calculada en milisegundos mediante el endpoint `/api/v1/ping` (ej. `2 ms` en cable LAN, `12 ms` en Wi-Fi 5GHz).
   - **Velocidad Pico y Promedio**: Registrada en MB/s durante transferencias activas y guardada como promedio histórico.
   - **Estado de Presencia**: `"En línea ahora"`, `"Visto hace X minutos"`, o `"Desconectado"`.
2. **Estadísticas de Uso Acumuladas**:
   - **Volumen Total de Datos**: Bytes enviados (`totalBytesSent`) y bytes recibidos (`totalBytesReceived`).
   - **Contador de Archivos**: Total de archivos transferidos con éxito.
   - **Tasa de Confiabilidad**: Porcentaje de transferencias completadas con éxito frente a cancelaciones o fallos de red.
   - **Línea de Tiempo**: Fecha exacta de vinculación inicial (`pairedAt`) y último contacto registrado (`lastSeenAt`).

### 3.5 Sistema de Bloqueos y Lista Negra (Blacklist & Quarantine)
Protege al usuario contra nodos maliciosos, spam en redes públicas o intentos no autorizados:

1. **Tipos de Bloqueo**:
   - **Bloqueo Manual**: El usuario puede bloquear cualquier dispositivo directamente desde la lista de vínculos o desde el radar de dispositivos cercanos.
   - **Bloqueo Automático por Fuerza Bruta (Rate Limiting)**:
     - 3 intentos consecutivos de PIN incorrecto -> Cuarentena temporal de **5 minutos**.
     - 10 intentos fallidos acumulados -> Bloqueo permanente automático a la lista negra.
2. **Comportamiento ante Nodos Bloqueados**:
   - **Ocultamiento en UI**: El dispositivo bloqueado no se muestra en el radar de dispositivos cercanos.
   - **Descarte Silencioso (Silent Drop)**: Cualquier petición HTTP entrante (`/ping`, `/pair/init`, `/session/request`) proveniente de una IP o UUID en la lista negra se rechaza inmediatamente con `403 Forbidden` o cierre abrupto de socket TCP, evitando fugas de información.
3. **Panel de Gestión de Bloqueos (En Ajustes)**:
   - Vista dedicada para revisar dispositivos bloqueados con fecha de bloqueo, motivo (manual o fuerza bruta) y botón para **"Desbloquear"**.

---

## 4. Endpoints y Contrato de Transporte (HTTP sobre TLS 1.3)

El servicio expone un servidor HTTP interno en el puerto anunciado (por defecto `4242`):

### 4.1 `GET /api/v1/ping`
Verificación de presencia, salud y estado de disponibilidad del nodo.
- **Respuesta 200 OK**:
  ```json
  {
    "status": "ready",
    "id": "uuid-del-nodo",
    "name": "Edro-PC",
    "os": "windows",
    "version": "1.0.0"
  }
  ```

### 4.2 `POST /api/v1/pair/init`
Inicia el desafío de emparejamiento. El receptor genera y muestra el PIN en pantalla.
- **Cuerpo de Solicitud (Request)**:
  ```json
  {
    "requester": {
      "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
      "name": "Pixel 9 Pro",
      "os": "android",
      "fingerprint": "sha256:7a8f90b1c2..."
    }
  }
  ```
- **Respuesta 200 OK**:
  ```json
  {
    "status": "awaiting_pin",
    "expiresInSeconds": 60
  }
  ```

### 4.3 `POST /api/v1/pair/verify`
El iniciador envía el PIN que el usuario digitó.
- **Cuerpo de Solicitud (Request)**:
  ```json
  {
    "requesterId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "enteredPin": "492815"
  }
  ```
- **Respuestas posibles**:
  - `200 OK`: PIN correcto. Devuelve `{ "status": "paired", "pairingToken": "token-hash-uuid..." }`.
  - `401 Unauthorized`: PIN incorrecto (`{ "error": "invalid_pin", "attemptsRemaining": 2 }`).
  - `403 Forbidden`: Bloqueado por exceder intentos o rechazado manualmente.
  - `408 Request Timeout`: El PIN ha expirado (>60 segundos).

### 4.4 `POST /api/v1/session/request`
Inicia una propuesta de transferencia. **Requiere que los nodos estén previamente emparejados** (incluyendo el encabezado `X-CrossRelay-Pairing-Token`). El emisor envía la lista de archivos para que el receptor acepte o rechace antes de enviar bytes pesados.
- **Cuerpo de Solicitud (Request)**:
  ```json
  {
    "sessionId": "b3e0c442-98fc-4242-b001-123456789abc",
    "sender": {
      "id": "uuid-sender",
      "name": "Pixel 9 Pro",
      "os": "android"
    },
    "verificationPin": "4892",
    "totalFiles": 2,
    "totalBytes": 104857600,
    "files": [
      {
        "fileId": "file-1",
        "name": "backup_db.tar.gz",
        "size": 100000000,
        "mimeType": "application/gzip",
        "sha256": "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8"
      },
      {
        "fileId": "file-2",
        "name": "readme.md",
        "size": 4857600,
        "mimeType": "text/markdown",
        "sha256": "4b227777d4dd1fc61c6f884f48641d02b4d121d3fd328cb08b5531fcacdabf8a"
      }
    ]
  }
  ```
- **Respuestas posibles**:
  - `200 OK`: Aceptado por el receptor (o auto-aceptado por ser de confianza).
  - `403 Forbidden`: Rechazado manualmente por el usuario receptor.
  - `409 Conflict`: El receptor ya está ocupado en otra transferencia.
  - `507 Insufficient Storage`: Espacio en disco insuficiente en el receptor.

### 4.5 `POST /api/v1/session/{sessionId}/upload/{fileId}`
Streaming del archivo binario.
- **Encabezados**:
  - `Content-Type: application/octet-stream`
  - `Content-Length: <tamaño_exacto_del_archivo>`
  - `X-CrossRelay-Sha256: <hash_sha256>`
- **Cuerpo**: Flujo binario directo en chunks continuos.
- **Respuesta 200 OK**:
  ```json
  {
    "fileId": "file-1",
    "status": "completed",
    "bytesReceived": 100000000,
    "verified": true
  }
  ```

### 4.6 `POST /api/v1/session/{sessionId}/cancel`
Cancela la sesión en curso de forma inmediata en el receptor (invocado por el emisor al cancelar o abortar una transferencia activa).
- **Respuesta 200 OK**:
  ```json
  {
    "status": "cancelled"
  }
  ```

---

## 5. Streaming y Control de Integridad

1. **Tamaño de Chunks recomendado**:
   - Mínimo: **64 KB**.
   - Óptimo para LAN Gigabit / Wi-Fi 6: **512 KB a 1 MB**.
2. **Modo Batch Stream para Lotes Masivos (Problema de los 10k archivos pequeños)**:
   - Para transferencias con alta cantidad de archivos (ej. miles de fotos/documentos de 1 MB):
     - El emisor **no realiza peticiones HTTP individuales por cada archivo** para evitar la penalización de latencia y round-trips.
     - Se utiliza un **flujo binario continuo multiplexado (Tar-Stream al vuelo)** sobre un único socket TLS 1.3 sin pausas.
     - El receptor Go Core lee el flujo con streaming directo a disco (`io.CopyBuffer` / buffer pooling) y verificación SHA-256 al vuelo, permitiendo saturar enlaces de 400 MB/s sin cuellos de botella de red.
3. **Limitador de Ancho de Banda Configurable (Bandwidth Throttling)**:
   - El motor de streaming incorpora un algoritmo de Token Bucket con cuotas por milisegundo:
     - `Sin Límite` (Máxima saturación que soporte el hardware / NIC).
     - `100 MB/s`.
     - `50 MB/s`.
     - `10 MB/s`.
     - `Personalizado` (valor numérico en MB/s).
4. **Validación Hash SHA-256**:
   - El emisor calcula el hash al leer el archivo.
   - El receptor calcula el hash al vuelo mientras escribe los bytes a disco.
   - Al finalizar el último byte, si los hashes no coinciden, el receptor elimina el archivo temporal y responde con `422 Unprocessable Entity`.
5. **Almacenamiento Temporal**:
   - Los archivos en recepción se guardan con extensión `.crossrelay.part`.
   - Solo al validarse el hash al 100% se renombra al nombre final del archivo.

---

## 6. API Local de Control IPC (`/local/v1/...`)

Contrato HTTP local (loopback `127.0.0.1`) entre la interfaz Desktop (Avalonia) y el demonio Core Go (`control/handler.go`):

| Método | Ruta | Propósito |
| :--- | :--- | :--- |
| `GET` | `/local/v1/status` | Estado del nodo local, puerto TLS activo (`activePort`), identidad y peers descubiertos. |
| `GET` | `/local/v1/settings` | Obtiene la configuración activa del nodo. |
| `POST` | `/local/v1/settings` | Guarda y aplica configuración del nodo (`deviceName`, `downloadDirectory`, `visibility`, `speedLimit`, `port`, `portMode`, `portRangeEnd`, `duplicatePolicy`). |
| `GET` | `/local/v1/trusted` | Lista todos los equipos vinculados y sus estadísticas/políticas. |
| `POST` | `/local/v1/trusted/update` | Actualiza alias, carpeta dedicada o política de aceptación (`AutoAccept` / `AskConfirmation`). |
| `POST` | `/local/v1/trusted/block` | Bloquea o desbloquea un equipo por ID. |
| `DELETE` | `/local/v1/trusted/{id}` | Desvincula y elimina un equipo de confianza. |
| `POST` | `/local/v1/pair/init` | Inicia el desafío de vinculación PIN de 6 dígitos hacia un peer remoto. |
| `POST` | `/local/v1/pair/verify` | Envía el PIN digitado al peer remoto para completar la vinculación. |
| `POST` | `/local/v1/peers/probe` | Sondea manualmente una IP/puerto (`/api/v1/ping`) cuando mDNS/broadcast está filtrado. |
| `POST` | `/local/v1/diagnostics/ports` | Diagnostica estado de puertos candidatos (`active_self`, `free`, `occupied`) y estado básico de firewall. |
| `POST` | `/local/v1/send` | Inicia el envío asíncrono de un lote de archivos/carpetas hacia un peer vinculado. |
| `POST` | `/local/v1/transfer/respond` | Acepta o rechaza una solicitud entrante bajo política `AskConfirmation`. |
| `POST` | `/local/v1/transfer/pause` | Pausa o reanuda el envío saliente activo (`{"paused": true \| false}`). |
| `POST` | `/local/v1/transfer/cancel` | Cancela el envío saliente activo y notifica `POST /api/v1/session/{sessionId}/cancel` al receptor. |
| `POST` | `/local/v1/receive/cancel` | Aborta las sesiones entrantes activas desde la vista `Recibir` del receptor. |
| `GET` | `/local/v1/events` | Stream Server-Sent Events (SSE) con progreso de transferencias, desafíos PIN, vínculos y presencia de peers. |

---

## 7. Reglas Mandatorias de Implementación

1. **Fase de Emparejamiento Previo con PIN Digitado (Zero-Trust Anti-Fatiga)**:
   - Todo nodo descubierto debe estar en estado `Unpaired` por defecto.
   - Para vincular dos nodos, el receptor genera un PIN de 6 dígitos que expira en 60 segundos (`/api/v1/pair/init`).
   - El usuario en el nodo emisor debe **digitar activamente el PIN** en pantalla (`/api/v1/pair/verify`). No se permite la confirmación por simple clic "Sí".
   - Máximo 3 intentos de digitación antes de aplicar bloqueo temporal por fuerza bruta.
   - Ninguna sesión de transferencia (`/api/v1/session/request`) puede iniciarse sin el `pairingToken` resultante.
2. **Gestión de Vínculos, Telemetría y Políticas de Confianza**:
   - Todo dispositivo emparejado debe ser administrable localmente (alias editable, carpeta de descarga dedicada, revocación).
   - Políticas por dispositivo: `AutoAccept` o `AskConfirmation`.
   - Registro de telemetría: latencia RTT (ms), velocidad pico/promedio, bytes/archivos acumulados y tasa de éxito.
3. **Sistema de Bloqueos y Lista Negra**:
   - Soporte de bloqueo manual y automático (rate-limiting por fuerza bruta de PIN).
   - Descarte silencioso (`403 Forbidden` o corte TCP) de peticiones de nodos bloqueados y ocultamiento en radar.
4. **Paridad de Protocolo**:
   - Tanto el Core Go (`core/`) como los clientes (Desktop C# / Android Kotlin) deben respetar exactamente estos endpoints, estructuras JSON y contratos.
5. **Cero Texto Plano**:
   - Ninguna transferencia de archivos ni emparejamiento P2P puede ocurrir sin un canal TLS 1.3 activo.
6. **Manejo de Archivos Parciales**:
   - Escribir siempre en `.crossrelay.part` hasta verificar SHA-256; eliminar inmediatamente en caso de fallo o cancelación.
7. **Control de Flujo y Cancelación**:
   - Toda operación de socket, pausa o streaming debe responder de inmediato a cancelación (`context.Context` en Go, `CancellationToken` en C#, `CoroutineContext` / `Job` en Kotlin).
