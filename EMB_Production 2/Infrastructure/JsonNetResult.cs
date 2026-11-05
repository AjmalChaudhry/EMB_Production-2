using System;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EMB_Production.Infrastructure
{
    public class JsonNetResult : JsonResult
    {
        public Formatting Formatting { get; set; } = Formatting.None;

        public JsonSerializerSettings SerializerSettings { get; set; }

        public JsonNetResult()
        {
            SerializerSettings = new JsonSerializerSettings
            {
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = 5 // Prevent too deep recursion
            };
        }

        public override void ExecuteResult(ControllerContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            HttpResponseBase response = context.HttpContext.Response;
            response.ContentType = !string.IsNullOrEmpty(ContentType)
                ? ContentType
                : "application/json";

            if (ContentEncoding != null)
                response.ContentEncoding = ContentEncoding;

            if (Data == null) return;

            try
            {
                // Use streaming writer to reduce memory footprint
                var serializer = JsonSerializer.Create(SerializerSettings);
                using (var writer = new JsonTextWriter(response.Output) { Formatting = Formatting })
                {
                    serializer.Serialize(writer, Data);
                    writer.Flush();
                }
            }
            catch (OutOfMemoryException)
            {
                // Optional: log error and return a smaller message instead of crashing
                response.Write("{\"error\":\"Data too large to serialize.\"}");
            }
        }
    }
}
