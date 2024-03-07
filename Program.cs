using System;
using System.Collections.Generic;
using SimpleHttp;
using isci.Allgemein;
using isci.Beschreibung;
using System.Threading.Tasks;
using isci.Daten;
using System.Text;

namespace isci.modellaustausch
{
    class Konfiguration : Parameter
    {
        [fromEnv, fromArgs]
        public int Port;

        [fromEnv, fromArgs]
        public int Zyklus;

        public Konfiguration(string[] args) : base(args)
        {

        }
    }

    class Program
    {
        static Dictionary<string, long> LokaleModelle(string pfad)
        {
            // TODO: Struktur der Modellnamen mit anderen Modulen abgleichen!
            var modelle = new Dictionary<string, long>();
            var files = System.IO.Directory.GetFiles(pfad);
            foreach (var file in files)
            {
                var Modell = isci.Daten.Datenmodell.AusDatei(file);
                var filename = System.IO.Path.GetFileName(file);
                modelle.Add(filename, Modell.Stand);
            }
            return modelle;
        }

        static async Task Main(string[] args)
        {
            var konfiguration = new Konfiguration(args);

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.modellaustausch");
            beschreibung.Name = "Modellaustausch " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zum Modellaustausch";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            
            var vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);

            SimpleHttp.Route.Add("/datenmodelle", (rq, rp, args) => {
                var obj = new Newtonsoft.Json.Linq.JObject();
                var files = System.IO.Directory.GetFiles(konfiguration.OrdnerDatenmodelle);
                foreach (var file in files)
                {
                    var Modell = isci.Daten.Datenmodell.AusDatei(file);
                    var filename = System.IO.Path.GetFileName(file);
                    obj.Add(filename, Modell.Stand);
                }
                rp.StatusCode = 200;
                rp.ContentEncoding = Encoding.UTF8;
                byte[] bytes = Encoding.UTF8.GetBytes(obj.ToString());
                rp.ContentLength64 = bytes.Length;
                rp.ContentType = "application/json";
                rp.OutputStream.Write(bytes, 0, bytes.Length);
                rp.Close();
            });

            SimpleHttp.Route.Add("/modelle/{model}", (rq, rp, args) => {
                args["model"] = Uri.UnescapeDataString(args["model"]);

                System.Console.WriteLine("Abruf Datenmodel " + args["model"]);
                try {
                    if (args["model"] == "" || args["model"].Contains("/")){
                         rp.AsText("{}", "application/json");
                    }

                    var content = System.IO.File.ReadAllText(konfiguration.OrdnerDatenmodelle + "/" + args["model"]);
                    rp.StatusCode = 200;
                    rp.ContentEncoding = Encoding.UTF8;
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    rp.ContentLength64 = bytes.Length;
                    rp.ContentType = "application/json";
                    rp.OutputStream.Write(bytes, 0, bytes.Length);

                } catch(Exception ex){
                    Console.WriteLine("Fehler bei der Abfrage des Modells: " + ex.Message);
                    Console.WriteLine("Gebe leeres Modell und Fehler 404 zurück.");
                    rp.StatusCode = 404;
                    rp.AsText("{}", "application/json");
                }
                rp.Close();
            });

            // HttpServer im Hintergrund laufen lassen
            HttpServer.ListenAsync(konfiguration.Port, System.Threading.CancellationToken.None, Route.OnHttpRequestAsync);
            

            var discovery = new isci.mdnsDiscovery(konfiguration, "modellaustausch", konfiguration.Port);

            discovery.Bewerben();
            discovery.Entdecken();

            discovery.starteThread();

            var client = new System.Net.Http.HttpClient();

            while(true)
            {
                Console.WriteLine("Warte " + konfiguration.Zyklus + "ms");
                System.Threading.Thread.Sleep(konfiguration.Zyklus);
                //discovery.Bewerben();
                if (!discovery.Anwendungen.ContainsKey(konfiguration.Anwendung.ToLower())) continue; // to lower, da MDNS alles klein schreibt

                foreach (var Anwendung_Entdeckung in discovery.Anwendungen[konfiguration.Anwendung.ToLower()])
                {
                    if(Anwendung_Entdeckung.Port == null){
                        throw new ArgumentNullException("Port der Anwendung nicht gesetzt, überprüfe MDNS-Discovery. Eventuell falsch geparst?");
                    }
                    if(Anwendung_Entdeckung.Modul == null){
                        throw new ArgumentNullException("Modul der Anwendung nicht gesetzt, überprüfe MDNS-Discovery. Eventuell falsch geparst?");
                    }
                    if(Anwendung_Entdeckung.Ressource == null){
                        throw new ArgumentNullException("Ressource der Anwendung nicht gesetzt, überprüfe MDNS-Discovery. Eventuell falsch geparst?");
                    }



                    var uri = new Uri("http://" + Anwendung_Entdeckung.Ipv4 + ":" + Anwendung_Entdeckung.Port + "/datenmodelle");

                    var request = client.GetStringAsync(uri);
                    
                    try
                    {
                        
                        string response = await client.GetStringAsync(uri);
                    }
                    catch (System.Exception)
                    {
                        
                        throw;
                    }


                    var verfuegbare_modelle = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Result);

                    foreach (var verfuegbar in verfuegbare_modelle)
                    {
                        uri = new Uri("http://" + Anwendung_Entdeckung.Ipv4 + ":" + Anwendung_Entdeckung.Port + "/modelle/" + verfuegbar.Key);
                        Console.WriteLine("Frage Modell von " + uri.ToString() + " ab.");
                        string newModel = await client.GetStringAsync(uri);
                        
                        if (newModel != "{}")
                        {
                            // Convert string newModel to JObject
                            var tmpJObj = Newtonsoft.Json.Linq.JObject.Parse(newModel);

                            // Parse JObject to Datenmodell
                            Datenmodell remoteModel = Datenmodell.AusJObject(tmpJObj);


                            // Wenn Modell schon lokal gespeichert ist, prüfen ob Stand gleich ist
                            if (vorhandeneModelle.ContainsKey(verfuegbar.Key))
                            {
                                long lokalerStand = vorhandeneModelle[verfuegbar.Key];

                                // TODO: Stand in isci.Datenmodell verwenden/implementieren
                                if (remoteModel.Stand > lokalerStand)
                                {
                                    // wenn Remote neuer ist, speichern
                                    System.IO.File.WriteAllText(konfiguration.OrdnerDatenmodelle + "/" + verfuegbar.Key, newModel);
                                    vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);                                      
                                }
                            }
                            // Ansonsten immer speichern und lokale Modelle aktualisieren
                            else
                            {
                                System.IO.File.WriteAllText(konfiguration.OrdnerDatenmodelle + "/" + verfuegbar.Key, newModel);
                                vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);    
                            }
                        }
                    }
                }
            }            
        }
    }
}
