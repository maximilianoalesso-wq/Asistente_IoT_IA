using Microsoft.VisualBasic;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

//SYSTEM PROMPT para el Agente IA
string SystemPrompt = """
                "#System prompt: 
                ##Rol
                Eres un asistente de IoT (Internet de las cosas).
                Tu personalidad es amable y atenta.
                Tienes a tu cargo dos dispositivos que el humano desea controlar remotamente. Estos 
                dispositivos se controlan a través de los pines de salida de un mocrocontrolador ESP32.
                ##Listado de dispositivos:
                - pin 2 => luz del living
                - pin 12 => luz del comedor
                - pin 5 => ventilador
                - pin 14 => radio
                
                ## Reglas
                - Cuando el humano se refiera a la "luz del living", debes identificarla como el 
                dispositivo con pin 2.
                - Cuando el humano se refiera a la "luz del comedor", debes identificarla como el 
                dispositivo con pin 12.
                - Cuando el humano se refiera al "ventilador", debes identificarlo como el
                dispositivo con pin 5.
                - Cuando el humano se refiera a la "radio", debes identificarla como el dispositivo 
                con pin 14.
                - Nunca inventes un pin.
                - Solo puedes utilizar los pines 2, 12, 5 y 14.
                - Estos dispositivos se pueden encender (estado del pin = si) o apagar (estado del pin = no).
                
                ##Tareas
                -Debes ejecutar las órdenes del humano.
                -Si el humano solicita información sobre el estado de los dispositivos, debes
                responderle en base al contexto que aparece más abajo sobre el estado de los dispositivos.
                -Si el humano solicita la activación de uno o varios dispositivos debes informar el
                estado con los que quedarán los dispositivos luego de aplicar dicha orden.
                -Siempre debes responder en formato JSON con la **estructura especificada en el campo/clave
                generationConfig**.
                -Si el humano enciende o apaga uno o más dispositivos (pin) debes informar el estado de todos los
                dispositivos en la clave/campo *estado_dispositivos* (JSON array) para ser procesado
                por un software y en la clave/campo "texto" un mensaje informando que se llevó a cabo 
                dicha acción en lenguage natural para enviar al humano.
                -Siempre incluye un texto en la clave \"texto\" el cual será mostrado al humano.
                -Si el humano pregunta sobre el estado de un dispositivo, incluye un mensaje en la clave
                "texto" además del JSON  en *estado_dispositivos* con el estado actual de los dispositivos, por ejemplo: 
                Humano: \"Cuál es el estado actual de los dispositivos?\"
                AI: pone este texto en el campo "texto": "Actualmente la luz del living y del comedor están apagadas, mientras que el ventilador
                y la radio estan encendidas". Además, establece el campo *estado_dispositivos* con un Json array 
                contenidondo el estado actual de los dispositivos.
                
                ##Guardrails
                -Siempre responde en base al contexto enviado.
                -No busques información en otro sitio.
                -Si te preguntan otra cosa diles con amabilidad que tu tarea está limitada a la función de IoT.
            """;


Console.WriteLine("Iniciando API agente IA IoT...");

app.MapPost("/api/mensaje", ResponderMensajeIoT);



async Task<IResult> ResponderMensajeIoT(EAWebhookBody body)
{
    string tipoMsj = body.data.messageType;
    string nroTelAux = body.data.key.remoteJid; //acá tiene el @
    int idx = nroTelAux.IndexOf("@");
    string nroTel = nroTelAux.Substring(0, idx);
    string textoConversacion;


    Console.WriteLine("\n\n");
    Console.WriteLine("---------------------");
    Console.WriteLine("Analizando mensaje...");



    await Task.Delay(15000); //ESPERA
    if (tipoMsj == "conversation") //Mensaje de texto normal
    {
        textoConversacion = body.data.message.conversation;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Texto recibido: {textoConversacion}");
        Console.ResetColor();   
        //enviar texto recibido a la IA junto con el estado actual de los dispositivos
        //armar campo text
        string systemPrompt = SystemPrompt;
        string estadoDispActual = await ConsultarEstadoDispositivos();
        //Console.WriteLine($"Estado dispositivos: {estadoDispActual}");
        systemPrompt += estadoDispActual;
        //enviar texto a la IA (y actualizar dispositivos)
        string respAHumano = await EnviarMensajeAIA(systemPrompt, textoConversacion);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Respuesta enviado al usuario: {respAHumano}");
        Console.ResetColor();
        //actualizar pines y enviar respuesta de la IA al usuario
        await EnviarMensajeWsp(nroTel, respAHumano);

    }
    else if (tipoMsj == "audioMessage")
    {
        string idMensaje = body.data.key.id;
        string audioBase64 = await ObtenerAudioBase64(idMensaje);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Se recibió un audio.");
        Console.ResetColor();
        //enviar texto recibido a la IA junto con el estado actual de los dispositivos
        //armar campo text
        string systemPrompt = SystemPrompt;
        string estadoDispActual = await ConsultarEstadoDispositivos();
        //Console.WriteLine($"Estado dispositivos: {estadoDispActual}");
        systemPrompt += estadoDispActual;
        //enviar texto a la IA (y actualizar dispositivos)
        string respAHumano = await EnviarMensajeAudioAIA(systemPrompt, audioBase64);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Respuesta enviado al usuario: {respAHumano}");
        Console.ResetColor();
        //actualizar pines y enviar respuesta de la IA al usuario
        await EnviarMensajeWsp(nroTel, respAHumano);

    }

    return Results.Ok(new
    {
        estado = "ok"
    });
}



app.Run("http://0.0.0.0:5000");

async Task<string> ObtenerAudioBase64(string idMensaje)
{
    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("apikey", "API_KEY_EVOLUTION_API-49AF-85A9-025D244D9303");
    var body = new
    {
        message = new
        {
            key = new
            {
                id = idMensaje
            }
        },
        convertToMp4 = false
    };

    var respuesta = await cliente.PostAsJsonAsync("http://localhost:8080/chat/getBase64FromMediaMessage/testiot", body);
    EAAudioBase64? base64Audio = await respuesta.Content.ReadFromJsonAsync<EAAudioBase64>();
    return base64Audio?.base64 ?? ""; 
}

async Task EnviarMensajeWsp(string nroTel, string texto)
{
    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("apikey", "API_KEY_EVOLUTION_API-49AF-85A9-025D244D9303");
    var body = new
    {
        number = nroTel,
        text = texto
    };

    await cliente.PostAsJsonAsync("http://localhost:8080/message/sendText/testiot", body);
}


async Task<string> ConsultarEstadoDispositivos()
{
    string estadoDisp = "";
    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("apikey", "API_KEY_SUPABASE.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imxkc3pveGttd3N0Z3ZhcmdvcmttIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzYxOTA3MTYsImV4cCI6MjA5MTc2NjcxNn0.cqjbbUQ7dRKOz6dKKYTXzdEuySXpqEGRhiW4ZrqdPao");
    var respuesta = await cliente.GetAsync("https://ldszoxkmwstargorkm.supabase.co/rest/v1/iot_dispositivos?select=pin,estado");
    List<Dispositivo> dispositivos = await respuesta.Content.ReadFromJsonAsync<List<Dispositivo>>();
    string jsonDisp = JsonSerializer.Serialize(dispositivos);
    estadoDisp = "\n\n##Contexto del estado actual de los dispositivos```json\n" + jsonDisp + "\n```";
    return estadoDisp;
}

//IA: enviar mensaje de texto
async Task<string> EnviarMensajeAIA(string systemPrompt, string textoHumano)
{
    var ObjPostIA = new
    {
        system_instruction = new
        {
            parts = new[] {
            new {
                text = systemPrompt
            }
        }
        },
        contents = new[]
        {
        new {
            role = "user",
            parts = new[] {
                new {
                    text = textoHumano
                }
            }
        }
    },
        generationConfig = new
        {
            responseMimeType = "application/json",
            responseSchema = new
            {
                type = "object",
                properties = new
                {
                    texto = new
                    {
                        type = "string"
                    },
                    estado_dispositivos = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                pin = new
                                {
                                    type = "integer"
                                },
                                estado = new
                                {
                                    type = "string",
                                    @enum = new[] { "si", "no" }
                                }
                            },
                            required = new[] { "pin", "estado" }
                        }
                    }
                },
                required = new[] { "texto", "estado_dispositivos" }
            }
        }
    }; 

    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("x-goog-api-key", "API_KEY_GOOGLE_GEMINI_JuOYuXgVMI6Fq4Ysc");
    var respuesta = await cliente.PostAsJsonAsync("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", ObjPostIA);
    if (respuesta.IsSuccessStatusCode)
    {
        RespuestaIA? respIA = await respuesta.Content.ReadFromJsonAsync<RespuestaIA>();
        if (respIA == null)
            return "Error de comunicación con la IA.";
        //obtener respuesta para el humano
        IATextEstado? iATextEstado = JsonSerializer.Deserialize<IATextEstado>(respIA.candidates[0].content.parts[0].text);
        string respHumano = iATextEstado.texto;
        List<Dispositivo> estadoDispositivos = iATextEstado.estado_dispositivos;
        //actualizar estado de dispositivos
        ActualizarEstadosDispositivos(estadoDispositivos);
        return respHumano;
    }
    else
    {
        return "Error de comunicación con la IA.";
    }
}

//IA: enviar audio
async Task<string> EnviarMensajeAudioAIA(string systemPrompt, string audioBase64Humano)
{
    var ObjPostIA = new
    {
        system_instruction = new
        {
            parts = new[] {
            new {
                text = systemPrompt
            }
        }
        },
        contents = new[]
        {
        new {
            role = "user",
            parts = new object[] {
                new {
                    text = "Escucha el audio del humano. Determina qué dispositivos desea controlar o qué acción solicita realizar."
                },
                new
                {
                    inline_data = new
                    {
                        mime_type = "audio/ogg",
                        data = audioBase64Humano
                    }
                }
            }
        }
    },
        generationConfig = new
        {
            responseMimeType = "application/json",
            responseSchema = new
            {
                type = "object",
                properties = new
                {
                    texto = new
                    {
                        type = "string"
                    },
                    estado_dispositivos = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                pin = new
                                {
                                    type = "integer"
                                },
                                estado = new
                                {
                                    type = "string",
                                    @enum = new[] { "si", "no" }
                                }
                            },
                            required = new[] { "pin", "estado" }
                        }
                    }
                },
                required = new[] { "texto", "estado_dispositivos" }
            }
        }
    };

    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("x-goog-api-key", "API_KEY_GOOGLE_GEMINI_JuOYuXgVMI6Fq4Ysc");
    var respuesta = await cliente.PostAsJsonAsync("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", ObjPostIA);
    if (respuesta.IsSuccessStatusCode)
    {
        RespuestaIA? respIA = await respuesta.Content.ReadFromJsonAsync<RespuestaIA>();
        if (respIA == null)
            return "Error de comunicación con la IA.";
        //obtener respuesta para el humano
        IATextEstado? iATextEstado = JsonSerializer.Deserialize<IATextEstado>(respIA.candidates[0].content.parts[0].text);
        string respHumano = iATextEstado.texto;
        List<Dispositivo> estadoDispositivos = iATextEstado.estado_dispositivos;
        //actualizar estado de dispositivos
        ActualizarEstadosDispositivos(estadoDispositivos);
        return respHumano;
    }
    else
    {
        return "Error de comunicación con la IA.";
    }
}

async Task ModificarEstadoDispositivo(int pin, string estado)
{
    using HttpClient cliente = new HttpClient();
    cliente.DefaultRequestHeaders.Add("apikey", "API_KEY_SUPABASE.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imxkc3pveGttd3N0Z3ZhcmdvcmttIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzYxOTA3MTYsImV4cCI6MjA5MTc2NjcxNn0.cqjbbUQ7dRKOz6dKKYTXzdEuySXpqEGRhiW4ZrqdPao");

    var body = new
    {
        estado
    };
    await cliente.PatchAsJsonAsync("https://ldsxkmwstgvargorkm.supabase.co/rest/v1/iot_dispositivos?pin=eq." + pin, body);
    Console.WriteLine($"- Pin: {pin} - Estado: {estado}.");
}


async Task ActualizarEstadosDispositivos(List<Dispositivo> estadoDisp)
{
    foreach (var disp in estadoDisp)
    {
        ModificarEstadoDispositivo(disp.pin, disp.estado);
    }
}

//CLASE Dispositivo
class Dispositivo
{
    public int pin { get; set; }
    public string estado { get; set; } //si, no
}

//Clase Evolution API respuesta getBase64FromMediaMessage
public class EAAudioBase64
{
    public string base64 { get; set; }
}

//CLASE EAWebhookBody
public class EAWebhookBody
{
    public Data data { get; set; }
}

public class Data
{
    //[JsonPropertyName("message")]
    public Message message { get; set; }
    public string messageType { get; set; } //conversation o pollUpdateMessage o audioMessage
    public Key key { get; set; }
}

public class Key
{
    public string remoteJid { get; set; } //número telefono origen: 5493424767316@s.whatsapp.net
    public string id { get; set; } //id necesario para recuperar el audio
}
public class Message
{
    public string conversation { get; set; } //aquí viene el texto en mensajes de texto
    public PollUpdateMessage pollUpdateMessage { get; set; }
}

public class PollUpdateMessage
{
    public Vote vote { get; set; }
}

public class Vote
{
    public List<string> selectedOptions { get; set; }
}

//CLASE RespuestaIA
class RespuestaIA
{
    public List<Candidates> candidates { get; set; }
}

class PartsText
{
    public string text { get; set; }
}

class Content
{
    public List<PartsText> parts { get; set; }
    public string role { get; set; }
}

class Candidates
 {
    public Content content { get; set; }
    public string finishReason { get; set; }
    public int index { get; set; }
}

//RespuestaIAText -> respIA.candidates[0].content.parts[0].text
class IATextEstado
{
    public string texto { get; set; }
    public List<Dispositivo> estado_dispositivos { get; set; }
}
