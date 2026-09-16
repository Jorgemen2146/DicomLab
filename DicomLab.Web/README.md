# DICOM Lab Web

Monolito ASP.NET Core MVC, .NET 9, Razor y Bootstrap local. La consola original se conserva. Ambos proyectos usan `DicomLab.Core`, que compila los servicios existentes de `../Services` y `../Models` mediante archivos vinculados: no hay dos copias de la lógica DIMSE.

## Ejecutar

Desde la carpeta que contiene `pruebasdicom.csproj`:

```powershell
dotnet restore DicomLab.slnx
dotnet build DicomLab.slnx -m:1
dotnet run --project DicomLab.Web --launch-profile http
```

Abre **http://localhost:5262/Dicom**. En Visual Studio también puedes elegir `DicomLab.Web` como proyecto de inicio. La consola sigue ejecutándose con `dotnet run --project pruebasdicom.csproj`.

No inicies los mismos SCP simultáneamente desde consola y MVC: ambos usan 11112 y 11113. Puedes cerrar la consola e iniciar los SCP desde el dashboard, o dejar los SCP de consola activos y utilizar únicamente las pantallas cliente MVC.

## Flujo de prueba

1. **Viewer**: pulsa **Seleccionar DICOM**, elige un archivo ya extraído del ZIP en el diálogo del navegador y pulsa **Cargar DICOM**. No escribas nombres ni rutas. MVC recibe el contenido como `IFormFile`, valida el stream con fo-dicom y muestra el nombre cargado, metadata, tags y el PNG del frame seleccionado. Para multiframe usa Anterior/Siguiente. Window Center y Window Width se muestran desde el dataset; `DicomImage` utiliza su pipeline estándar y su comportamiento automático cuando faltan. Si no hay Pixel Data, aparece «Este objeto DICOM no contiene una imagen visualizable.» y se conservan los tags.
2. **Dashboard**: pulsa Iniciar LOCAL_PACS. Debe quedar Escuchando en 127.0.0.1:11112.
3. **C-ECHO**: conserva LOCAL_PACS / 127.0.0.1 / 11112 / DICOM_CLIENT y ejecuta. Se muestra el estado DICOM y su código hexadecimal.
4. **C-STORE**: pulsa **Seleccionar DICOM** y después **Enviar C-STORE**. El resultado muestra archivo, SOP Instance UID, SOP Class UID y estado DICOM. Si el envío falla después de validar el archivo, sus identificadores siguen visibles. Repite con varias instancias del mismo estudio. El SCP guarda en ReceivedDicoms y conserva su política de nombres únicos. El nombre del upload se limpia solo para mostrarlo y nunca se usa para escribir en disco.
5. **C-FIND**: deja los filtros vacíos para listar estudios o usa los criterios existentes: PatientID, PatientName, StudyInstanceUID, StudyDate, StudyDescription y Modality. Los filtros se combinan. La tabla devuelve un resultado por estudio; el enlace C-MOVE precarga su UID. Se conservan los comodines de texto y los rangos de fecha yyyyMMdd-yyyyMMdd del laboratorio.
6. **Dashboard**: inicia MOVE_DESTINATION en 127.0.0.1:11113.
7. **C-MOVE**: introduce el StudyInstanceUID y MOVE_DESTINATION. Tras terminar se muestra el historial de respuestas Pending/final, Remaining, Completed, Failed y Warning. Los archivos llegan mediante una segunda asociación C-STORE a MovedDicoms. No se copian directamente entre carpetas ni se eliminan del PACS.

El dashboard muestra las rutas absolutas. En MVC se resuelven contra el directorio del proyecto web, no contra el directorio de trabajo: por defecto apuntan a ReceivedDicoms y MovedDicoms del proyecto de consola.

En el resultado de **C-STORE** también aparece **Ruta del archivo guardado** cuando el receptor es un SCP iniciado desde este dashboard. Es la ruta real confirmada después de guardar y verificar el archivo, incluido el nombre único generado por el servidor. Se relaciona con la solicitud mediante los AE Titles, el Message ID y el SOP Instance UID. Si el receptor es un PACS externo o un SCP ejecutado en otro proceso (por ejemplo la consola), se indica que la ruta no está disponible: el protocolo C-STORE no la devuelve.

## Rutas MVC

| Ruta | Métodos | Función |
| --- | --- | --- |
| `/Dicom` | GET | Dashboard y estado de los SCP |
| `/Dicom/Viewer` | GET, POST | Upload, metadata, tags y navegación de frames |
| `/Dicom/Image?id=...&frame=0` | GET | PNG del frame, índice base cero |
| `/Dicom/Echo` | GET, POST | C-ECHO con nodo y Calling AE editables |
| `/Dicom/Find` | GET, POST | C-FIND Study Root, nivel STUDY |
| `/Dicom/Move` | GET, POST | C-MOVE Study Root, nivel STUDY |
| `/Dicom/Store` | GET, POST | Upload y C-STORE |
| `/Dicom/StartServer` | POST | Iniciar PACS o destino local |

## Configuración

`appsettings.json`, sección `Dicom`, configura LocalAeTitle, Pacs, MoveDestination, ReceivedDirectory, MovedDirectory y los límites del visor. Los campos de cada formulario pueden cambiar el nodo remoto para esa operación. La resolución de MOVE_DESTINATION en el SCP se toma de esta configuración.

- Upload: 100 MB por archivo por defecto; se valida también la cantidad real de bytes leídos.
- Caché del visor: 256 MB, compartida por la aplicación, con caducidad por inactividad de 20 minutos. Si se llena se expulsan las cargas menos recientes.
- Máximo por frame: 16 777 216 píxeles, configurable.
- Los uploads del visor se mantienen en memoria y se aíslan por sesión mediante un identificador aleatorio. No se guardan permanentemente. El parser vuelve a abrir el contenido en un stream durante cada lectura/renderizado y solo renderiza el frame solicitado.
- Los formularios POST requieren antiforgery. Las respuestas del visor no se almacenan en caché HTTP. Razor codifica los valores de tags al generar HTML.
- La extensión y el nombre del archivo no determinan su validez: se intenta abrir con fo-dicom y se comprueban los UID de clase e instancia y que no esté parcialmente leído.
- Los SCP se mantienen como servicios singleton y se liberan al detener el host web. Su inicio se serializa para evitar carreras entre solicitudes.

## Paquetes y renderizado

- `fo-dicom` **5.2.6**, en DicomLab.Core.
- `fo-dicom.Imaging.ImageSharp` **5.2.6**, en MVC.
- `SixLabors.ImageSharp` **3.1.11**, dependencia transitiva del paquete de renderizado.
- ASP.NET Core MVC y DI pertenecen al framework compartido de .NET 9.
- Bootstrap se sirve desde `wwwroot/lib`, sin CDN.

Se registra `ImageSharpImageManager` y se usa `DicomImage.RenderImage(frame).AsSharpImage()` para generar PNG. Las APIs se contrastaron con el [paquete oficial 5.2.6](https://www.nuget.org/packages/fo-dicom.Imaging.ImageSharp/5.2.6) y el [código de DicomImage de esa versión](https://github.com/fo-dicom/fo-dicom/blob/5.2.6/FO-DICOM.Core/Imaging/DicomImage.cs).

## Archivos y reutilización

Creado: `DicomLab.Core/DicomLab.Core.csproj`, `Models/DicomMetadata.cs`, y el proyecto MVC con:

- `Program.cs`, `appsettings.json` y `Properties/launchSettings.json`.
- `Controllers/DicomController.cs`, `DicomController.Network.cs` y `HomeController.cs` para errores generales.
- `Services/Dicom/DicomFileService.cs`, `DicomImageService.cs`, `DicomNetworkService.cs` y `DicomLocalServers.cs`.
- `Models/DicomLabOptions.cs`, `ViewModels/ViewerViewModel.cs` y `NetworkViewModel.cs`.
- `Views/Dicom/Index.cshtml`, `Viewer.cshtml`, `Network.cshtml`, vistas compartidas, Bootstrap y scripts `viewer.js`/`network.js`.

Modificado: solución `../pruebasdicom.slnx`, `pruebasdicom.csproj`, `Models/DicomNodeOptions.cs`, los seis archivos de servicios compartidos y `README.md`. La consola mantiene su menú y selección por número; sus servicios ahora admiten resultados estructurados, Calling AE configurable, cancelación de cliente y salida a ILogger mediante un callback.

## Comprobaciones y límites

Se probaron mediante HTTP: dashboard y formularios, upload con nombre arbitrario, metadata, PNG de dos frames diferentes, frame inválido, aislamiento de sesión, path traversal, archivo inválido, upload grande, DICOM sin píxeles, C-ECHO, C-STORE, C-FIND, C-MOVE con contadores, destino desconocido, PACS apagado y antiforgery. También se ejecutaron las pruebas de regresión de consola.

El visor maneja las sintaxis admitidas por los paquetes instalados. No se han añadido codecs nativos adicionales: algunos DICOM comprimidos pueden mostrar un error de renderizado, manteniendo disponibles sus metadatos. Tampoco se implementan herramientas de diagnóstico, mediciones ni ajustes manuales de ventana.

Los contadores C-MOVE se muestran al terminar la petición HTTP, no se transmiten en tiempo real al navegador. Se mantienen los límites de tiempo existentes: C-ECHO 30 segundos, C-FIND/C-STORE dos minutos y C-MOVE hasta seis minutos en el cliente. Cancelar la petición web no equivale a implementar C-CANCEL DIMSE.

Es una aplicación de laboratorio local sin autenticación de usuarios ni configuración de TLS DICOM. El aislamiento de uploads por sesión no sustituye autenticación. El límite de caché no es un límite de memoria total del proceso: también hay buffers de carga y renderizado.

## Laboratorio DICOMweb

Se mantienen la consola, Core, el visor y todos los servicios DIMSE. La nueva carpeta `Services/DicomWeb` implementa un cliente HTTP mediante `AddHttpClient<DicomWebService>` / `IHttpClientFactory`. fo-dicom 5.2.6 se utiliza para validar y leer objetos; no se utiliza su cliente DIMSE para enviar HTTP.

### Configuración y servidor de pruebas

En `appsettings.json`:

```json
"DicomWeb": {
  "BaseUrl": "http://localhost:8042/dicom-web/",
  "TimeoutSeconds": 60,
  "MaxResponseMb": 100
}
```

La URL es un ejemplo. Cada formulario permite cambiarla para esa operación y la navegación de resultados conserva la dirección. No se persiste ese cambio en appsettings. Se aceptan HTTP y HTTPS con validación normal del certificado, sin credenciales en la URL, query ni fragmento. El timeout configurable también cubre la lectura del cuerpo de respuesta.

Hace falta un servidor DICOMweb accesible desde el proceso ASP.NET Core: por ejemplo [Orthanc con el plugin DICOMweb](https://orthanc.uclouvain.be/book/plugins/dicomweb.html). Debe tener habilitados STOW, QIDO y WADO bajo el prefijo configurado. El SCP local `LOCAL_PACS:11112` solamente ofrece DIMSE; iniciarlo no crea endpoints HTTP. No se ha instalado ni iniciado Orthanc como parte de esta ampliación. El laboratorio todavía no envía autenticación: un servidor que la exija responderá 401/403. Se puede añadir posteriormente un `DelegatingHandler` en el registro del cliente para Bearer u otros headers sin reescribir las operaciones.

### Ejecutar y probar

Desde la carpeta que contiene `DicomLab.slnx`:

```powershell
dotnet restore DicomLab.slnx
dotnet build DicomLab.slnx -m:1
dotnet run --project DicomLab.Web/DicomLab.Web.csproj
```

Abre la URL que indique `Now listening on`. Usa datos de prueba y extrae los archivos si todavía están dentro de un ZIP.

1. Inicia tu servidor DICOMweb. Abre `/DicomWeb` para ver la comparación de protocolos y entra en STOW-RS.
2. En `/DicomWeb/Stow`, configura la URL base, selecciona uno o varios `.dcm` y pulsa **Enviar STOW-RS**. Revisa HTTP Status, Reason Phrase, cantidad, UIDs y **Response body**. HTTP 202 puede representar almacenamiento parcial: inspecciona el cuerpo, incluyendo las secuencias de instancias fallidas del servidor. Un código HTTP exitoso no es una garantía de aceptación individual de todas las instancias.
3. En `/DicomWeb/Qido`, usa la misma URL y busca por `PatientName`, `PatientID`, `StudyDate`, `StudyInstanceUID` o `AccessionNumber`. Ejemplo: `PatientName=TEST*`. StudyDate utiliza `YYYYMMDD` o un rango `YYYYMMDD-YYYYMMDD`. Dejar filtros vacíos consulta estudios sin filtros.
4. Pulsa **Ver series** en un estudio y **Ver instancias** en una serie. Los resultados muestran los UIDs y atributos principales; un atributo ausente aparece como `N/A`.
5. Pulsa **Descargar DICOM** en una instancia. WADO recupera sus bytes y entrega un archivo `<SOPInstanceUID>.dcm` al navegador. El navegador determina la carpeta de descarga; esta acción no crea archivos en `ReceivedDicoms`.
6. Pulsa **Ver DICOM** para recuperar la misma instancia y abrirla en el Viewer existente. Se reutilizan `DicomFileService`, la caché por sesión y `DicomImageService`: metadata, tags y PNG por frame. No se crea un segundo visor ni una copia permanente en el servidor.
7. También puedes entrar directamente en `/DicomWeb/Wado` y escribir Study Instance UID, Series Instance UID y SOP Instance UID para descargar o visualizar.
8. El menú **DIMSE** conserva C-ECHO, C-STORE, C-FIND y C-MOVE. Sus AE Titles, puertos y directorios siguen dependiendo de la sección `Dicom`, de forma independiente de `DicomWeb`.

### Rutas MVC y requests remotos

| Ruta MVC | Métodos | Función |
|---|---|---|
| `/DicomWeb` | GET | Comparación educativa |
| `/DicomWeb/Stow` | GET, POST | Formulario y envío STOW |
| `/DicomWeb/Qido` | GET, POST | Formulario y consulta de estudios |
| `/DicomWeb/Series` | POST | Consulta de series desde el estudio |
| `/DicomWeb/Instances` | POST | Consulta de instancias desde la serie |
| `/DicomWeb/Wado` | GET, POST | Formulario, descarga o apertura en el visor |

Los POST MVC de formularios incluyen antiforgery. Sus métodos son independientes del método usado por el servicio remoto; por ejemplo, el POST MVC de QIDO genera un GET DICOMweb.

Ejemplo de STOW remoto (boundary ilustrativo):

```http
POST /dicom-web/studies HTTP/1.1
Host: localhost:8042
Accept: application/dicom+json
Content-Type: multipart/related; boundary=dicom-example; type="application/dicom"

--dicom-example
Content-Type: application/dicom

[bytes DICOM Part 10 de la primera instancia]
--dicom-example
Content-Type: application/dicom

[bytes DICOM Part 10 de la segunda instancia]
--dicom-example--
```

El navegador carga archivos al MVC con `multipart/form-data`. El servicio valida todos los archivos antes del envío y construye una nueva petición **multipart/related** hacia el PACS; no reenvía el formulario como STOW.

Ejemplos QIDO remotos (los parámetros se codifican para URL):

```http
GET /dicom-web/studies?PatientName=TEST%2A&includefield=00081030 HTTP/1.1
Accept: application/dicom+json

GET /dicom-web/studies/1.2.3/series?includefield=0008103E HTTP/1.1
Accept: application/dicom+json

GET /dicom-web/studies/1.2.3/series/1.2.4/instances?includefield=00080016 HTTP/1.1
Accept: application/dicom+json
```

El helper interpreta atributos por tags hexadecimales, sus arrays `Value`, nombres PN (Alphabetic/Ideographic/Phonetic), valores numéricos y campos ausentes. Los `includefield` solicitan los atributos adicionales mostrados en las tablas.

Ejemplo WADO remoto:

```http
GET /dicom-web/studies/1.2.3/series/1.2.4/instances/1.2.5 HTTP/1.1
Accept: multipart/related; type="application/dicom"; transfer-syntax=*
```

Se analiza el boundary MIME y se exige una única parte `application/dicom`. También se admite una respuesta directa `application/dicom` por compatibilidad. fo-dicom valida el objeto y se comprueba que sus tres UIDs coincidan con los solicitados antes de descargar o visualizar. No se interpreta el DICOM binario como JSON.

Referencias: DICOM PS3.18 [Store](https://dicom.nema.org/medical/dicom/current/output/chtml/part18/sect_10.5.html), [Search](https://dicom.nema.org/medical/dicom/current/output/chtml/part18/sect_10.6.html) y [Retrieve](https://dicom.nema.org/medical/dicom/current/output/chtml/part18/sect_10.4.html).

### Comparación técnica

| Propósito | DIMSE existente | DICOMweb nuevo |
|---|---|---|
| Guardar | C-STORE mediante asociación DICOM/TCP | STOW-RS mediante POST HTTP |
| Buscar | C-FIND con respuestas Pending/Success | QIDO-RS con GET y DICOM JSON |
| Recuperar | C-MOVE entrega mediante C-STORE a otro AE | WADO-RS devuelve bytes en la respuesta GET |

C-GET también tiene propósito de recuperación en DIMSE, pero no está implementado aquí. C-ECHO verifica una asociación DICOM; no equivale a un servicio DICOMweb estándar de eco. DICOMweb no necesita Calling/Called AE Title en estas peticiones: identifica recursos mediante URL y UIDs.

### Archivos de la ampliación

Creados bajo `DicomLab.Web`:

- `Controllers/DicomWebController.cs`.
- `Services/DicomWeb/DicomWebService.cs`, `DicomWebService.Query.cs`, `DicomWebService.Retrieve.cs`.
- `Models/DicomWebOptions.cs`, `Models/DicomWebResult.cs`, `ViewModels/DicomWebViewModel.cs`.
- `Views/DicomWeb/Index.cshtml`, `Stow.cshtml`, `Qido.cshtml`, `Wado.cshtml`, `_Endpoint.cshtml`, `_Result.cshtml`.
- `wwwroot/js/dicomweb-stow.js`.

Modificados: `Program.cs`, `appsettings.json`, `Views/Shared/_Layout.cshtml` y este `README.md`. No se modifican los servicios DIMSE, Core ni la consola para agregar DICOMweb.

### Validación y límites DICOMweb

Se comprobó el servicio con respuestas controladas y el MVC contra un servidor HTTP simulado: STOW múltiple, MIME y bytes, filtros QIDO codificados, PN/números/campos ausentes, navegación de los tres niveles, WADO multipart y directo, integridad de la descarga, apertura en el Viewer y generación de PNG. Se verificaron HTTP 400/401/403/404/500, conexión fallida, timeout, JSON/multipart inválidos, DICOM inválido y UIDs distintos, además de antiforgery. La regresión MVC del Viewer, C-ECHO, C-STORE, C-FIND y C-MOVE también pasó. Estas pruebas no sustituyen una prueba de interoperabilidad con el PACS DICOMweb que se vaya a utilizar.

- Sin autenticación HTTP todavía, OAuth ni Azure AD. Los redirects no se siguen automáticamente.
- STOW: hasta 100 archivos; tamaño total según `Dicom:MaxUploadMb` (100 MB por defecto). ASP.NET puede usar buffers temporales para uploads que elimina al terminar la petición.
- WADO: una instancia completa por petición. `MaxResponseMb` limita la respuesta HTTP completa, incluyendo MIME; la instancia también respeta `Dicom:MaxUploadMb`. Los buffers son de memoria y no representan un límite global de memoria del proceso.
- QIDO y cuerpos de error/STOW: máximo 4 MB; el texto visible se abrevia a 64.000 caracteres. No hay paginación ni recuperación de estudios completos, frames remotos o bulk data. El servidor puede limitar los resultados de búsqueda.
- STOW muestra la respuesta del servidor sin un resumen automático por instancia de sus secuencias de fallo/advertencia.
- El visor conserva las limitaciones de codecs y renderizado ya documentadas. Solicitar `transfer-syntax=*` permite recibir sintaxis que pueden necesitar codecs adicionales para visualizarse.
- El laboratorio realiza HTTP desde el servidor MVC hacia la URL indicada; no se requiere CORS del PACS para estas llamadas. Debe usarse en el entorno local previsto: no incorpora control de usuarios ni restricciones de destinos para exposición pública.
