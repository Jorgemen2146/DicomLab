using FellowOakDicom;
using pruebasdicom.Services;
using pruebasdicom.Models;

var dicomPath = args.Length > 0 ? args[0] :
    @"C:\Users\USUARIO\Downloads\Anonymized_20260916\series-00002\image-00000.dcm";
var receivedDirectory = Path.GetFullPath("ReceivedDicoms");
var movedDirectory = Path.GetFullPath("MovedDicoms");
var pacsNode = new DicomNodeOptions("LOCAL_PACS", "127.0.0.1", 11112);
var moveNode = new DicomNodeOptions("MOVE_DESTINATION", "127.0.0.1", 11113);
var pacsOptions = new DicomServerOptions
{
    Node = pacsNode,
    StorageDirectory = receivedDirectory,
    MoveDestinations = new(StringComparer.Ordinal) { [moveNode.AeTitle] = moveNode }
};
var dicomDirectory = Path.GetDirectoryName(Path.GetFullPath(dicomPath))!;
new DicomSetupBuilder().RegisterServices(s => s.AddFellowOakDicom()).Build();
var reader = new DicomReaderService();
var client = new DicomClientService(pacsNode);
using var server = new DicomServerService(pacsOptions);
using var moveServer = new DicomMoveDestinationService(moveNode, movedDirectory);
Console.WriteLine($"Archivo configurado: {dicomPath}");
Console.WriteLine($"Carpeta de recepción: {receivedDirectory}");
Console.WriteLine("Si el archivo está dentro de un ZIP, primero debes extraerlo.");
while (true)
{
    Console.WriteLine("\n==============================\nDICOM LAB\n==============================");
    Console.WriteLine("1 - Leer DICOM\n2 - Mostrar todos los tags\n\n----- DIMSE -----\n3 - Iniciar LOCAL_PACS SCP\n4 - Ejecutar C-ECHO\n5 - Enviar DICOM con C-STORE\n6 - Buscar estudios con C-FIND\n7 - Iniciar MOVE_DESTINATION SCP\n8 - Ejecutar C-MOVE\n9 - Mostrar configuración DICOM\n\n0 - Salir");
    Console.Write("Opción: ");
    var option = Console.ReadLine();
    if (option is null or "0") break;
    try
    {
        switch (option)
        {
            case "1":
                Console.Write("Número del archivo (ejemplo: 1 = image-00001.dcm): ");
                var input = Console.ReadLine();
                if (input is null) return;
                if (!int.TryParse(input, out var number) || number < 0)
                {
                    Console.WriteLine("Ingresa un número entero mayor o igual a 0.");
                    break;
                }
                var selectedPath = Path.Combine(dicomDirectory, $"image-{number:D5}.dcm");
                await reader.ReadAsync(selectedPath);
                dicomPath = selectedPath;
                Console.WriteLine($"Archivo seleccionado: {Path.GetFileName(dicomPath)} (también para las opciones 2 y 5).");
                break;
            case "2": await reader.DumpAsync(dicomPath); break;
            case "3": await server.StartAsync(); break;
            case "4":
                if (!server.IsListening) Console.WriteLine("Inicia primero el SCP con la opción 3.");
                else await client.EchoAsync();
                break;
            case "5":
                if (!server.IsListening) Console.WriteLine("Inicia primero el SCP con la opción 3.");
                else await client.StoreAsync(dicomPath);
                break;
            case "6":
                if (!server.IsListening) { Console.WriteLine("Inicia primero LOCAL_PACS con la opción 3."); break; }
                Console.WriteLine("Filtros opcionales: ENTER para no filtrar. Texto: * y ? como comodines.");
                var patientId = Prompt("PatientID");
                var patientName = Prompt("PatientName");
                var studyUid = Prompt("StudyInstanceUID");
                var studyDate = Prompt("StudyDate (yyyyMMdd o yyyyMMdd-yyyyMMdd)");
                var description = Prompt("StudyDescription");
                var modality = Prompt("Modality (por ejemplo CT)");
                await client.FindAsync(patientId, patientName, studyUid, studyDate, description, modality);
                break;
            case "7": await moveServer.StartAsync(); break;
            case "8":
                if (!server.IsListening) { Console.WriteLine("Inicia primero LOCAL_PACS con la opción 3."); break; }
                var moveStudyUid = Prompt("StudyInstanceUID");
                await client.MoveAsync(moveStudyUid, moveNode.AeTitle);
                break;
            case "9":
                Console.WriteLine($"Calling AE: {DicomClientService.CallingAe}");
                Console.WriteLine($"PACS: {pacsNode.AeTitle} -> {pacsNode.Host}:{pacsNode.Port} | Activo: {server.IsListening}");
                foreach (var (ae, node) in pacsOptions.MoveDestinations)
                    Console.WriteLine($"Destino: {ae} -> {node.Host}:{node.Port}");
                Console.WriteLine($"MOVE_DESTINATION activo: {moveServer.IsListening}");
                Console.WriteLine($"ReceivedDicoms: {receivedDirectory}\nMovedDicoms: {movedDirectory}\nArchivo seleccionado: {dicomPath}");
                break;
            default: Console.WriteLine("Opción no válida."); break;
        }
    }
    catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
}

static string Prompt(string label)
{
    Console.Write($"{label}: ");
    return Console.ReadLine()?.Trim() ?? "";
}
