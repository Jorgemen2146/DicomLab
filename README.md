# DICOM Lab

## Inicio rápido de la aplicación web

```powershell
git clone https://github.com/Jorgemen2146/DicomLab.git
cd DicomLab
dotnet restore DicomLab.slnx
dotnet build DicomLab.slnx -m:1
dotnet run --project DicomLab.Web --launch-profile http
```

Abre http://localhost:5262/Dicom. Requiere un SDK compatible con .NET 9 y soluciones `.slnx` (por ejemplo SDK .NET 9.0.200 o posterior) y el runtime ASP.NET Core 9. Los archivos DICOM de prueba se proporcionan localmente y no se incluyen en el repositorio. Los archivos recibidos, compilaciones y configuración local están excluidos mediante `.gitignore`.

Laboratorio de consola .NET 9 con fo-dicom **5.2.6**. Se conserva el nombre original `pruebasdicom.csproj`.

La solución incluye ahora **DicomLab.Web**, un monolito MVC que conserva y reutiliza los servicios de consola mediante **DicomLab.Core**. Consulta [las instrucciones del laboratorio web](DicomLab.Web/README.md) para iniciar el visor, los SCP y las operaciones DIMSE desde el navegador. La consola no se ha eliminado.

## Ejecutar la prueba

1. Extrae el ZIP en el Explorador de Windows. Una ruta como `...\_20260916.zip\series-00002\image-00000.dcm` no es una ruta de archivo accesible para este programa.
2. Abre PowerShell en la carpeta que contiene `pruebasdicom.csproj`.
3. Ejecuta:

   ```powershell
   dotnet restore
   dotnet build
   dotnet run -- "C:\Users\USUARIO\Downloads\Anonymized_20260916\series-00002\image-00000.dcm"
   ```

   Sustituye la ruta por la ubicación real después de extraer. También puedes editar `dicomPath` en `Program.cs` y ejecutar `dotnet run` sin argumentos.

4. Selecciona **1** e ingresa el número del archivo: `0` abre `image-00000.dcm`, `1` abre `image-00001.dcm` y `25` abre `image-00025.dcm`, siempre en la carpeta de la ruta configurada. Se muestran sus tags; los ausentes o vacíos aparecen como `N/A`. El archivo leído queda seleccionado también para las opciones **2** y **5**. Si el número no es válido o el archivo no se puede leer, se muestra el error y se conserva la selección anterior.
5. Selecciona **2** para recorrer el dataset. Las secuencias se muestran con sangría; los píxeles y bloques binarios se resumen. Los valores largos se abrevian y hay un límite de 16 niveles de secuencias.
6. Selecciona **3**. Debe aparecer `Listening on 127.0.0.1:11112`. El SCP permanece activo mientras usas el menú.
7. Selecciona **4**. Verás `C-ECHO received by SCP` y `C-ECHO Response: Success`.
8. Selecciona **5**. Verás `C-STORE received`, `Saved: ...`, la lectura del archivo guardado y `C-STORE Response: Success`.
9. Repite **5**: se creará otro archivo sin sobrescribir el anterior. El nombre combina SOP Instance UID y un GUID; `FileMode.CreateNew` impide sobrescrituras.
10. Revisa `ReceivedDicoms/` en la carpeta de trabajo desde la que ejecutaste el programa. La ruta absoluta se muestra al iniciar y al guardar. Selecciona **0** para cerrar ambos SCP y salir.

## C-FIND: buscar estudios

1. Inicia LOCAL_PACS con **3** y comprueba C-ECHO con **4**.
2. Usa **1** y **5** para enviar uno o varios archivos al PACS.
3. Selecciona **6**. Introduce filtros opcionales: PatientID, PatientName, StudyInstanceUID, StudyDate, StudyDescription y Modality. ENTER deja cada filtro vacío. Todos los filtros indicados se combinan.
4. Para texto puedes usar `*` y `?`. PatientName ignora mayúsculas/minúsculas; PatientID se compara respetándolas. StudyInstanceUID se compara exactamente. StudyDate admite `yyyyMMdd`, `yyyyMMdd-yyyyMMdd` y rangos abiertos como `20260901-`. Modality se consulta mediante `ModalitiesInStudy`, el atributo correspondiente a nivel STUDY.
5. Verás una respuesta Pending por estudio con PatientID, PatientName, StudyDate, StudyDescription, StudyInstanceUID y ModalitiesInStudy; al terminar, Success. Cero coincidencias también termina con Success.
6. Copia el StudyInstanceUID del estudio que quieres recuperar.

El índice se reconstruye leyendo `ReceivedDicoms/` y sus subcarpetas en cada consulta. Los archivos corruptos, incompletos o sin los UID necesarios se omiten con un mensaje; los atributos opcionales ausentes se muestran como N/A. Los archivos del mismo estudio se agrupan en un resultado. No se utiliza base de datos.

## C-MOVE: recuperar un estudio

1. Mantén LOCAL_PACS activo. Selecciona **7** para iniciar MOVE_DESTINATION en `127.0.0.1:11113`.
2. Selecciona **8** y pega el StudyInstanceUID obtenido mediante C-FIND.
3. El cliente solicita al PACS enviar ese estudio al AE Title `MOVE_DESTINATION`.
4. El PACS resuelve el AE Title mediante `pacsOptions.MoveDestinations`, configurado en `Program.cs`, y abre una nueva asociación hacia el destino. Envía las instancias mediante C-STORE. Las copias repetidas de un mismo SOP Instance UID se envían una sola vez por solicitud.
5. La consola muestra respuestas Pending con Remaining, Completed, Failed y Warning. Solo devuelve Success si todas las suboperaciones finalizan correctamente. Las fallidas se incluyen en FailedSOPInstanceUIDList; un destino desconocido devuelve A801. Un destino conocido pero apagado genera un resultado no exitoso con suboperaciones fallidas.
6. Revisa la carpeta `MovedDicoms/`, cuya ruta absoluta puedes consultar con **9**. Los archivos originales permanecen en ReceivedDicoms: C-MOVE no los elimina.
7. Un StudyInstanceUID sin coincidencias produce Success con cero suboperaciones. **0** cierra ambos servidores.

## Asociaciones y configuración

| Asociación | Solicitud | Respuestas |
| --- | --- | --- |
| DICOM_CLIENT → LOCAL_PACS | C-ECHO o C-STORE | Verificación o confirmación del almacenamiento |
| DICOM_CLIENT → LOCAL_PACS | C-FIND Study Root, nivel STUDY | Metadatos por estudio (Pending), después Success |
| DICOM_CLIENT → LOCAL_PACS | C-MOVE Study Root, nivel STUDY | Contadores Pending y resultado final |
| LOCAL_PACS → MOVE_DESTINATION | C-STORE generado por C-MOVE | Estado de almacenamiento por instancia |

La asociación de C-MOVE permanece abierta mientras el PACS envía los archivos al destino mediante la asociación C-STORE. Se incluyen Move Originator AE Title y Message ID para relacionar las suboperaciones con la solicitud original.

La opción **9** muestra los nodos, las rutas y el estado de ambos SCP. Las rutas de almacenamiento son relativas al directorio de trabajo: al ejecutar desde el IDE pueden quedar bajo `bin/Debug/net9.0/`. Para configurar nodos y destinos, edita los objetos `DicomNodeOptions` y el diccionario en `Program.cs`.

El alcance sigue siendo local, Study Root y nivel STUDY. Los tiempos máximos son dos minutos para C-FIND y cinco minutos para las transferencias de C-MOVE. No se implementa una interfaz de cancelación DIMSE ni consultas a nivel SERIES/IMAGE.

## Código y conceptos

| Concepto | Significado en este laboratorio |
| --- | --- |
| Reader | `DicomReaderService` abre archivos, muestra los tags principales y recorre el dataset. |
| SCU (Service Class User) | `DicomClientService`: cliente que solicita C-ECHO y C-STORE. |
| SCP (Service Class Provider) | `DicomServerService` mantiene el servidor; `LocalDicomScp`, en el mismo archivo, atiende las asociaciones y solicitudes. |
| AE Title | Nombre lógico de una aplicación DICOM: `DICOM_CLIENT` llama a `LOCAL_PACS`. Es distinto de IP y puerto. |
| C-ECHO | Operación para verificar comunicación DICOM con el SCP. |
| C-STORE | Operación para transferir una instancia DICOM al SCP para almacenarla. |
| SOP Instance UID | Identificador único de un objeto DICOM concreto. Reenviar el mismo objeto conserva este UID. |
| Study Instance UID | Identificador del estudio que agrupa series. |
| Series Instance UID | Identificador de la serie dentro del estudio; agrupa instancias. |

El SCP solo escucha en loopback y comprueba el Called AE Title. Acepta Verification y clases de almacenamiento reconocidas por fo-dicom. Conserva la sintaxis de transferencia negociada sin renderizar ni transcodificar píxeles; no necesita un visor ni codecs de imagen para este flujo. El archivo recibido puede tener metadatos de archivo distintos: la comprobación es de contenido DICOM, no de identidad byte a byte.

APIs contrastadas con el [código oficial de fo-dicom 5.2.6](https://github.com/fo-dicom/fo-dicom/tree/5.2.6/FO-DICOM.Core) y la documentación XML del paquete instalado.
