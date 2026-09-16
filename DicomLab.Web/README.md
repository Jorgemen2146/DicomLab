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
