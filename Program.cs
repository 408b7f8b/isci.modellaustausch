using System;
using SimpleHttp;
using isci.Allgemein;
using isci.Beschreibung;

namespace isci.modellaustausch
{
    class Program
    {
        static void Main(string[] args)
        {
            var konfiguration = new Parameter("konfiguration.json");

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.modellaustausch");
            beschreibung.Name = "Modellaustausch " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zum Modellaustausch";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            SimpleHttp.Route.Add("/datenmodelle", (rq, rp, args) => {
                var obj = new Newtonsoft.Json.Linq.JObject();
                var files = System.IO.Directory.GetFiles(konfiguration.OrdnerDatenmodelle, "*" + konfiguration.Anwendung + "*.json");
                foreach (var file in files)
                {
                    var Modell = isci.Daten.Datenmodell.AusDatei(file);
                    var filename = System.IO.Path.GetFileName(file);
                    obj.Add(filename, Modell.Stand);
                }
                rp.AsText(obj.ToString(), "application/json");
            });

            SimpleHttp.Route.Add("/{model}", (rq, rp, args) => {
                try {
                    if (args["model"] == "" || args["model"].Contains("/")) rp.AsText("{}", "application/json");
                    var content = System.IO.File.ReadAllText(konfiguration.OrdnerDatenmodelle + "/" + args["model"]);
                    rp.AsText(content, "application/json");
                } catch {
                    rp.AsText("{}", "application/json");
                }
            });

            SimpleHttp.HttpServer.ListenAsync(8086, System.Threading.CancellationToken.None, Route.OnHttpRequestAsync).Wait();

            var discovery = new isci.mdnsDiscovery();

            discovery.Bewerben(konfiguration, "modellaustausch", 8086);
            discovery.Entdecken();

            discovery.starteThread();

            while(true)
            {



                System.Threading.Thread.Sleep(60000);
            }            
        }
    }
}
