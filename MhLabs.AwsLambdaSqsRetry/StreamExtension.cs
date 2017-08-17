using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace MhLabs.AwsLambdaSqsRetry
{
    public static class StreamExtension
    {
        public static T DeserializeStream<T>(this Stream stream)
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
            }
        }
    }
}
