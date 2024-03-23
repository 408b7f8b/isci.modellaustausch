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
        public int warteZeit;

        public Konfiguration(string[] args) : base(args)
        {

        }
    }

    class Program
    {
        static Dictionary<string, string> LokaleModelle(string pfad)
        {
            // TODO: Struktur der Modellnamen mit anderen Modulen abgleichen!
            var modelle = new Dictionary<string, string>();
            var files = System.IO.Directory.GetFiles(pfad);
            foreach (var file in files)
            {
                var Modell = isci.Daten.Datenmodell.AusDatei(file);
                var filename = System.IO.Path.GetFileName(file);
                modelle.Add(filename, Modell.Stand);
            }
            return modelle;
        }
        static Dictionary<string, long> lokaleSchnittstellen(string pfad)
        {
            // TODO: Struktur der Modellnamen mit anderen Modulen abgleichen!
            var schnittstellen = new Dictionary<string, long>();
            var files = System.IO.Directory.GetFiles(pfad);
            foreach (var file in files)
            {
                var filename = System.IO.Path.GetFileName(file);
                var lastWrite = System.IO.File.GetLastWriteTimeUtc(file).Ticks;
                schnittstellen.Add(filename, lastWrite);
            }
            return schnittstellen;
        }

        static async Task Main(string[] args)
        {
            var konfiguration = new Konfiguration(args);

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.modellaustausch");
            beschreibung.Name = "Modellaustausch " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zum Modellaustausch";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");
            
            var vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);
            var vorhandeneSchnittstellen = lokaleSchnittstellen(konfiguration.OrdnerSchnittstellen);

            addRoutes(konfiguration);

            // HttpServer im Hintergrund laufen lassen
            HttpServer.ListenAsync(konfiguration.Port, System.Threading.CancellationToken.None, Route.OnHttpRequestAsync);
            
            var discovery = new mdnsDiscovery(konfiguration, "modellaustausch", konfiguration.Port);

            discovery.Bewerben();
            discovery.Entdecken();
            discovery.starteThread();

            var client = new System.Net.Http.HttpClient();

            while(true)
            {
                Logger.Debug("Warte " + konfiguration.warteZeit + "ms");
                System.Threading.Thread.Sleep(konfiguration.warteZeit);
                //discovery.Bewerben();
                if (!discovery.Anwendungen_ZugehoerigeEntdeckungen.ContainsKey(konfiguration.Anwendung.ToLower())) continue; // to lower, da MDNS alles klein schreibt

                foreach (var Anwendung_Entdeckung in discovery.Anwendungen_ZugehoerigeEntdeckungen[konfiguration.Anwendung.ToLower()])
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

                    Uri baseUri = new Uri("http://" + Anwendung_Entdeckung.Ipv4 + ":" + Anwendung_Entdeckung.Port);

                    Uri schnittstellenÜbersicht = new Uri(baseUri, "/schnittstellen");
                    Uri modellÜbersicht = new Uri(baseUri, "/datenmodelle");
                    
                    string response = "";
                    
                    try
                    {
                        response = await client.GetStringAsync(modellÜbersicht);
                    }
                    catch (System.Exception)
                    {
                        Console.WriteLine("Fehler bei Abfrage der Modellliste. Wahrscheinlich keine Verbindung möglich");
                        break;
                    }
                    var verfuegbare_modelle = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(response);

                    foreach (var verfuegbar in verfuegbare_modelle)
                    {
                        Uri modellUri = new Uri(baseUri, "/datenmodell/" + verfuegbar.Key);
                        Logger.Debug("Frage Modell von " + modellUri.ToString() + " ab.");

                        string newModel = await client.GetStringAsync(modellUri);
                        
                        if (newModel != "{}")
                        {
                            // Convert string newModel to JObject
                            var tmpJObj = Newtonsoft.Json.Linq.JObject.Parse(newModel);

                            // Parse JObject to Datenmodell
                            Datenmodell remoteModel = Datenmodell.AusJObject(tmpJObj);

                            // Wenn Modell schon lokal gespeichert ist, prüfen ob Stand gleich ist
                            if (vorhandeneModelle.ContainsKey(verfuegbar.Key))
                            {
                                string lokalerStand = vorhandeneModelle[verfuegbar.Key];

                                // TODO: Stand in isci.Datenmodell verwenden/implementieren
                                if (remoteModel.Stand != lokalerStand)
                                {
                                    // wenn Remote neuer ist, speichern
                                    System.IO.File.WriteAllText(konfiguration.OrdnerDatenmodelle + "/" + verfuegbar.Key, newModel);
                                    vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);
                                    Logger.Debug("Modell " + verfuegbar.Key + " aktualisiert.");
                                }else
                                {
                                    Logger.Debug("Modell " + verfuegbar.Key + " ist aktuell.");
                                }
                            }
                            // Ansonsten immer speichern und lokale Modelle aktualisieren
                            else
                            {
                                System.IO.File.WriteAllText(konfiguration.OrdnerDatenmodelle + "/" + verfuegbar.Key, newModel);
                                vorhandeneModelle = LokaleModelle(konfiguration.OrdnerDatenmodelle);
                                Logger.Debug("Modell " + verfuegbar.Key + " neu hinzugeüfgt.");
                            }
                        }
                    }

                    response = "";
                    try
                    {
                        response = await client.GetStringAsync(schnittstellenÜbersicht);
                    }
                    catch (System.Exception e)
                    {
                        Logger.Fehler("Ausnahme bei Abfrage der Schnittstellenliste. Wahrscheinlich keine Verbindung möglich. Text: " + e.Message);
                        break;
                    }
                    var verfuegbareSchnittstellen = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(response);
                    
                    foreach (var verfuegbar in verfuegbareSchnittstellen)
                    {
                        Uri modellUri = new Uri(baseUri, "/schnittstelle/" + verfuegbar.Key);
                        Logger.Debug("Frage Schnittstelle von " + modellUri.ToString() + " ab.");

                        string neueSchnittstelle = await client.GetStringAsync(modellUri);
                        
                        if (neueSchnittstelle != "{}")
                        {
                            // Parse Text to SchnittstelleUdp
                            SchnittstelleUdp remoteSchnittstelle = Newtonsoft.Json.JsonConvert.DeserializeObject<SchnittstelleUdp>(neueSchnittstelle);

                            // Wenn Schnittstelle schon lokal gespeichert ist, prüfen ob sie in den letzten 15 Minuten aktualisiert wurde
                            if (vorhandeneSchnittstellen.ContainsKey(verfuegbar.Key))
                            {
                                long lokalerStand = vorhandeneSchnittstellen[verfuegbar.Key];
                                
                                //Get UTC time - 15 Minuten in Ticks
                                //long utcMinusDelay = DateTime.UtcNow.Ticks - 900000000;
                                long utcMinusDelay = DateTime.UtcNow.Ticks - 900000000;
                                
                                if (lokalerStand < utcMinusDelay)
                                {
                                    // wenn Schnittstelle älter als 15 Minuten ist, aktualisieren
                                    System.IO.File.WriteAllText(konfiguration.OrdnerSchnittstellen + "/" + verfuegbar.Key, neueSchnittstelle);
                                    vorhandeneSchnittstellen = lokaleSchnittstellen(konfiguration.OrdnerSchnittstellen);
                                    Logger.Debug("Schnittstelle " + verfuegbar.Key + " aktualisiert.");
                                }else
                                {
                                    Logger.Debug("Schnittstelle " + verfuegbar.Key + " ist aktuell.");
                                }
                            }
                            // Ansonsten immer speichern und lokale Schnittstellen aktualisieren
                            else
                            {
                                System.IO.File.WriteAllText(konfiguration.OrdnerSchnittstellen + "/" + verfuegbar.Key, neueSchnittstelle);
                                vorhandeneSchnittstellen = lokaleSchnittstellen(konfiguration.OrdnerSchnittstellen);
                                Logger.Debug("Schnittstelle " + verfuegbar.Key + " neu hinzugefügt.");
                            }
                        }
                    }
                }
            }            
        }

        static void replyWithText(System.Net.HttpListenerResponse response, string text, string contentType="application/json")
        {
            response.StatusCode = 200;
            response.ContentEncoding = Encoding.UTF8;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            response.ContentLength64 = bytes.Length;
            response.ContentType = "application/json";
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        static async void addRoutes(Konfiguration konfiguration){
            SimpleHttp.Route.Add("/schnittstellen", (rq, rp, args) => {
                Logger.Debug("Anfrage für Liste an Schnittstellen");
                var obj = new Newtonsoft.Json.Linq.JObject();
                var files = System.IO.Directory.GetFiles(konfiguration.OrdnerSchnittstellen);
                foreach (var file in files)
                {
                    var Text = System.IO.File.ReadAllText(file);
                    var Schnittstelle = Newtonsoft.Json.JsonConvert.DeserializeObject<SchnittstelleUdp>(Text);
                    var filename = System.IO.Path.GetFileName(file);
                    obj.Add(filename, Schnittstelle.Identifikation);
                }
                replyWithText(rp, obj.ToString());
            });
            SimpleHttp.Route.Add("/schnittstelle/{schnittstelle}", (rq, rp, args) => {

                args["schnittstelle"] = Uri.UnescapeDataString(args["schnittstelle"]);

                Logger.Debug("Anfrage für Schnittstelle " + args["schnittstelle"]);

                try {
                    if (args["schnittstelle"] == "" || args["schnittstelle"].Contains("/")){
                         rp.AsText("{}", "application/json");
                    }
                    var content = System.IO.File.ReadAllText(konfiguration.OrdnerSchnittstellen + "/" + args["schnittstelle"]);
                    replyWithText(rp, content);
                } catch(Exception ex){
                    Logger.Fehler("Ausnahme bei der Anfrage für Schnittstelle: " + ex.Message + ". Gebe leeres Schnittstelle und Fehler 404 zurück.");
                    rp.StatusCode = 404;
                    rp.AsText("{}", "application/json");
                }
                rp.Close();
            });

            SimpleHttp.Route.Add("/datenmodelle", (rq, rp, args) => {
                Logger.Debug("Anfrage für Liste an Datenmodellen");
                var obj = new Newtonsoft.Json.Linq.JObject();
                var files = System.IO.Directory.GetFiles(konfiguration.OrdnerDatenmodelle);
                foreach (var file in files)
                {
                    var Modell = isci.Daten.Datenmodell.AusDatei(file);
                    var filename = System.IO.Path.GetFileName(file);
                    obj.Add(filename, Modell.Stand);
                }
                replyWithText(rp, obj.ToString());
            });

            SimpleHttp.Route.Add("/datenmodell/{model}", (rq, rp, args) => {
                args["model"] = Uri.UnescapeDataString(args["model"]);

                Logger.Debug("Anfrage für Datenmodell " + args["model"]);
                try {
                    if (args["model"] == "" || args["model"].Contains("/")){
                         rp.AsText("{}", "application/json");
                    }

                    var content = System.IO.File.ReadAllText(konfiguration.OrdnerDatenmodelle + "/" + args["model"]);
                    replyWithText(rp, content);

                } catch(Exception ex){
                    Logger.Fehler("Fehler bei der Anfrage für Modell: " + ex.Message + ". Gebe leeres Modell und Fehler 404 zurück.");
                    rp.StatusCode = 404;
                    rp.AsText("{}", "application/json");
                }
                rp.Close();
            });
        }
    }
}