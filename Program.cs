using System;
using System.Collections.Generic;
using SimpleHttp;
using isci.Allgemein;
using isci.Beschreibung;

namespace isci.modellaustausch
{
    class Konfiguration : Parameter
    {
        public int Port;
        public int Zyklus;

        public Konfiguration(string datei) : base(datei)
        {

        }
    }

    class Program
    {
        static Dictionary<string, string> LokaleModelle(string pfad, string anwendung)
        {
            var modelle = new Dictionary<string, string>();
            var files = System.IO.Directory.GetFiles(pfad, "*" + anwendung + "*.json");
            foreach (var file in files)
            {
                var Modell = isci.Daten.Datenmodell.AusDatei(file);
                var filename = System.IO.Path.GetFileName(file);
                modelle.Add(filename, Modell.Stand);
            }
            return modelle;
        }

        static void Main(string[] args)
        {
            var konfiguration = new Konfiguration("konfiguration.json");

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.modellaustausch");
            beschreibung.Name = "Modellaustausch " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zum Modellaustausch";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            var modelle = LokaleModelle(konfiguration.OrdnerDatenmodelle, konfiguration.Anwendung);

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

            SimpleHttp.Route.Add("/modelle/{model}", (rq, rp, args) => {
                try {
                    if (args["model"] == "" || args["model"].Contains("/")) rp.AsText("{}", "application/json");
                    var content = System.IO.File.ReadAllText(konfiguration.OrdnerDatenmodelle + "/" + args["model"]);
                    rp.AsText(content, "application/json");
                } catch {
                    rp.AsText("{}", "application/json");
                }
            });

            SimpleHttp.HttpServer.ListenAsync(8086, System.Threading.CancellationToken.None, Route.OnHttpRequestAsync);

            var discovery = new isci.mdnsDiscovery();

            discovery.Bewerben(konfiguration, "modellaustausch", konfiguration.Port);
            discovery.Entdecken();

            discovery.starteThread();

            var client = new System.Net.Http.HttpClient();

            while(true)
            {
                System.Threading.Thread.Sleep(konfiguration.Zyklus);

                if (!discovery.Anwendungen_Entdeckungen.ContainsKey(konfiguration.Anwendung)) continue;

                foreach (var Anwendung_Entdeckung in discovery.Anwendungen_Entdeckungen[konfiguration.Anwendung])
                {
                    var uri = new Uri(Anwendung_Entdeckung.Ipv4 + ":" + Anwendung_Entdeckung.Port + "/datenmodelle");

                    try {
                        var req = client.GetStringAsync(uri);
                        req.RunSynchronously();
                        var verfuegbare_modelle = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(req.Result);
                        var zu_holende_modelle = new Dictionary<string, string>();
                        foreach (var verfuegbar in verfuegbare_modelle)
                        {
                            if (modelle.ContainsKey(verfuegbar.Key))
                            {
                                if (int.Parse(modelle[verfuegbar.Key].Replace('.', '0')) >= int.Parse(verfuegbar.Value.Replace('.', '0'))) continue;
                            }

                            uri = new Uri(Anwendung_Entdeckung.Ipv4 + ":" + Anwendung_Entdeckung.Port + "/modelle/" + verfuegbar.Key);

                            try {
                                var down_req = client.GetStringAsync(uri);
                                req.RunSynchronously();
                                string file_content = req.Result;
                                if (req.Result != "{}")
                                {
                                    System.IO.File.WriteAllText(file_content, konfiguration.OrdnerDatenmodelle + "/" + verfuegbar.Key);
                                    modelle = LokaleModelle(konfiguration.OrdnerDatenmodelle, konfiguration.Anwendung);
                                }
                            } catch {

                            }
                        }
                    } catch {

                    }
                }
            }            
        }
    }
}
