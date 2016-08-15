using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

//Requires ESRI license
//TODO non esri versions i.e. grass
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geoprocessor;
using ESRI.ArcGIS.DataManagementTools;

namespace Create_LAI_Rasters
{
    class Program
    {
        public class LAI_Record
        {
            public DateTime date { get; set; }
            public double value { get; set; }

        }

        #region ESRI TOOLS
        private static LicenseInitializer m_AOLicenseInitializer = new Create_LAI_Rasters.LicenseInitializer();
        private static int RunTool(Geoprocessor geoprocessor, IGPProcess process, ITrackCancel TC)
        {

            // Set the overwrite output option to true
            geoprocessor.OverwriteOutput = true;
            //geoprocessor.SetEnvironmentValue("workspace", workspacefolder);
            // Execute the tool            
            try
            {
                geoprocessor.Execute(process, null);
                //ReturnMessages(geoprocessor);

            }
            catch (Exception err)
            {
                Console.WriteLine(err.Message);
                ReturnMessages(geoprocessor);
                return -1;
            }

            return 0;
        }
        private static void ReturnMessages(Geoprocessor gp)
        {
            if (gp.MessageCount > 0)
            {
                for (int Count = 0; Count <= gp.MessageCount - 1; Count++)
                {
                    Console.WriteLine(gp.GetMessage(Count));
                }
            }

        }
        #endregion

        static void Main(string[] args)
        {
            try
            {
                //TODO NEED TO HANDLE LANDCOVER TYPES NOT USED IN HYDROTERRE

                if (args.Count() != 4)
                {
                    Console.WriteLine("Arguments: <xml_input_filename> <landcover_input_filename> <lai_output_base_name> <output_directory> ");
                    Console.WriteLine("");
                    Console.WriteLine("xml_input_filename: lookup xml file from HydroTerre");
                    Console.WriteLine("landcover_input_filename: HydroTerre Landcover Raster Dataset");
                    Console.WriteLine("lai_output_base_name: Base filename for LAI Raster Files");
                    Console.WriteLine("output_directory: Directory where raster datasets will be created");
                    Console.WriteLine("");
                    return;
                }

                String xml_input_filename = args[0];
                String org_landcover_input_filename = args[1];
                String lai_output_base_name = args[2]; //"LAI_Month_";
                String output_location = args[3];


                DateTime start_time = DateTime.Now;
                Console.WriteLine("Start Time: " + start_time.ToShortTimeString());

                m_AOLicenseInitializer.InitializeApplication(new esriLicenseProductCode[] { esriLicenseProductCode.esriLicenseProductCodeArcServer }, new esriLicenseExtensionCode[] { });


                //key is landcover id
                Dictionary<int, List<LAI_Record>> lai_results = new Dictionary<int, List<LAI_Record>>();
                Dictionary<DateTime, String> list_field_calculations = new Dictionary<DateTime, string>();
                //key is datetime
                //Dictionary< landcover id, value>
                Dictionary<DateTime, Dictionary<int, double>> my_lai_results = new Dictionary<DateTime, Dictionary<int, double>>();
                int raster_count = 0;

                #region LOAD XML FILE
                XmlDocument input_xml_filename = new XmlDocument();
                input_xml_filename.Load(xml_input_filename);

                //TODO: CHANGE TO NAME BASE, NOT INDEX BASED
                int location_PIHM_Forcing_Lookup_node = 1;
                int location_Forcing_Outputs_node = 2;
                int location_LAI_List_node = 2;

                XmlNode xml_forcing_node = input_xml_filename.ChildNodes[location_PIHM_Forcing_Lookup_node];
                XmlNode xml_Forcing_Outputs_node = xml_forcing_node.ChildNodes[location_Forcing_Outputs_node];
                XmlNode xml_LAI_List_node = xml_Forcing_Outputs_node.ChildNodes[location_LAI_List_node];

                int lai_count = xml_LAI_List_node.ChildNodes.Count;

                foreach (XmlNode LAI_Group in xml_LAI_List_node.ChildNodes)
                {
                    XmlElement lai_element = LAI_Group["LandcoverID"];

                    int LandcoverID_value = Convert.ToInt32(lai_element.InnerText);

                    foreach (XmlNode find_record in LAI_Group.ChildNodes)
                    {
                        if (find_record.Name == "LAI_Record")
                        {
                            LAI_Record lr = new LAI_Record();
                            lr.date = Convert.ToDateTime(find_record["Date"].InnerText);
                            lr.value = Convert.ToDouble(find_record["Value"].InnerText);

                            if (lai_results.ContainsKey(LandcoverID_value))
                            {
                                lai_results[LandcoverID_value].Add(lr);
                            }
                            else
                            {
                                List<LAI_Record> lai_records = new List<LAI_Record>();
                                lai_records.Add(lr);
                                lai_results.Add(LandcoverID_value, lai_records);
                            }

                        }

                    }
                }
                #endregion

                #region Convert LAI Dictionary to timebased dictionary
                foreach (var v in lai_results)
                {
                    int lid = v.Key;
                    List<LAI_Record> records = v.Value;
                    foreach (var d in records)
                    {
                        if (my_lai_results.ContainsKey(d.date))
                        {
                            my_lai_results[d.date].Add(lid, d.value);
                        }
                        else
                        {
                            Dictionary<int, double> dict = new Dictionary<int, double>();
                            dict.Add(lid, d.value);
                            my_lai_results.Add(d.date, dict);
                        }
                    }
                }

                #endregion

                #region Create field calculation commands
                foreach (var v in my_lai_results)
                {

                    string codeblock = @"def Reclass(myLAI):\n";
                    foreach (var r in v.Value)
                    {
                        codeblock += "        if myLAI == " + r.Key + ":\n";
                        codeblock += "            return " + r.Value + "\n";
                    }
                    codeblock += "        return 0";

                    list_field_calculations.Add(v.Key, codeblock);
                }
                #endregion

                #region Create LAI Raster datasets
                foreach (var lai_dat in list_field_calculations)
                {
                    ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new Geoprocessor();
                    gp.OverwriteOutput = true;

                    String new_raster_file = output_location + "\\" + lai_output_base_name + raster_count + ".tif";

                    ESRI.ArcGIS.DataManagementTools.Copy copytool = new Copy();
                    copytool.in_data = org_landcover_input_filename;
                    copytool.out_data = new_raster_file;
                    int error_code = RunTool(gp, copytool, null);

                    raster_count++;

                    ESRI.ArcGIS.DataManagementTools.MakeRasterLayer makerasterlayer = new MakeRasterLayer();
                    makerasterlayer.in_raster = new_raster_file;
                    makerasterlayer.out_rasterlayer = "HT_LandCover2";
                    error_code = RunTool(gp, makerasterlayer, null);

                    ESRI.ArcGIS.DataManagementTools.AddField addfield = new AddField();
                    addfield.in_table = "HT_LandCover2";
                    addfield.field_name = "LAI";
                    addfield.field_type = "FLOAT";
                    error_code = RunTool(gp, addfield, null);

                    ESRI.ArcGIS.DataManagementTools.Delete del2 = new ESRI.ArcGIS.DataManagementTools.Delete();
                    del2.in_data = "HT_LandCover2";
                    error_code = RunTool(gp, del2, null);
                    del2 = null;
                    makerasterlayer = null;

                    makerasterlayer = new MakeRasterLayer();
                    makerasterlayer.in_raster = new_raster_file;
                    makerasterlayer.out_rasterlayer = "HT_LandCover2";
                    error_code = RunTool(gp, makerasterlayer, null);

                    ESRI.ArcGIS.DataManagementTools.CalculateField calcfield = new CalculateField();
                    calcfield.in_table = "HT_LandCover2";
                    calcfield.field = "LAI";
                    calcfield.expression = "!Value!";
                    calcfield.expression_type = "PYTHON_9.3";
                    error_code = RunTool(gp, calcfield, null);

                    del2 = new ESRI.ArcGIS.DataManagementTools.Delete();
                    del2.in_data = "HT_LandCover2";
                    error_code = RunTool(gp, del2, null);
                    del2 = null;
                    makerasterlayer = null;

                    makerasterlayer = new MakeRasterLayer();
                    makerasterlayer.in_raster = new_raster_file;
                    makerasterlayer.out_rasterlayer = "HT_LandCover2";
                    error_code = RunTool(gp, makerasterlayer, null);

                    calcfield = new CalculateField();
                    calcfield.in_table = "HT_LandCover2";
                    calcfield.field = "LAI";
                    calcfield.expression = "Reclass(!LAI!)";
                    calcfield.expression_type = "PYTHON_9.3";
                    calcfield.code_block = list_field_calculations.ElementAt(0).Value;
                    error_code = RunTool(gp, calcfield, null);


                    del2 = new ESRI.ArcGIS.DataManagementTools.Delete();
                    del2.in_data = "HT_LandCover2";
                    error_code = RunTool(gp, del2, null);
                    del2 = null;
                    makerasterlayer = null;
                }
                #endregion


                m_AOLicenseInitializer.ShutdownApplication();

                Console.WriteLine("Finished");
                DateTime end_time = DateTime.Now;
                TimeSpan ts = end_time - start_time;
                Console.WriteLine("End Time: " + end_time.ToShortTimeString());
                Console.WriteLine("Took: " + ts.TotalSeconds + " seconds");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

        }
    }
}
