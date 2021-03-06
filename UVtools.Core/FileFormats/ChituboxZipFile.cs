﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using UVtools.Core.Extensions;
using UVtools.Core.Operations;

namespace UVtools.Core.FileFormats
{
    public class ChituboxZipFile : FileFormat
    {
        #region Constants

        public const string GCodeStart = ";START_GCODE_BEGIN{0}" +
                                         "G21 ;Set units to be mm{0}" +
                                         "G90 ;Absolute Positioning{0}" +
                                         "M17 ;Enable motors{0}" +
                                         "G28 Z0 ;Home Z{0}" +
                                         //"G91 ;Relative Positioning{0}" +
                                         "M106 S0 ;Light off{0}" +
                                         ";START_GCODE_END{0}{0}";

        public const string GCodeEnd = ";END_GCODE_BEGIN{0}" +
                                      "M106 S0 ;Light off{0}" +
                                      "G1 Z{1} F25 ;Raize Z{0}" +
                                      "M18 ;Disable Motors{0}" +
                                      ";END_GCODE_END{0}";

        #endregion

        #region Sub Classes

        public class Header
        {
            // ;(****Build and Slicing Parameters****)
            [DisplayName("fileName")] public string Filename { get; set; } = string.Empty;
            [DisplayName("machineType")] public string MachineType { get; set; } = "Default";
            [DisplayName("estimatedPrintTime")] public float EstimatedPrintTime { get; set; }
            [DisplayName("volume")] public float Volume { get; set; }
            [DisplayName("resin")] public string Resin { get; set; } = "Normal";
            [DisplayName("weight")] public float Weight { get; set; }
            [DisplayName("price")] public float Price { get; set; }
            [DisplayName("layerHeight")] public float LayerHeight { get; set; }
            [DisplayName("resolutionX")] public uint ResolutionX { get; set; }
            [DisplayName("resolutionY")] public uint ResolutionY { get; set; }
            [DisplayName("machineX")] public float MachineX { get; set; }
            [DisplayName("machineY")] public float MachineY { get; set; }
            [DisplayName("machineZ")] public float MachineZ { get; set; }
            [DisplayName("projectType")] public string ProjectType { get; set; } = "Normal";
            [DisplayName("normalExposureTime")] public float LayerExposureTime { get; set; } = 7; // 35s
            [DisplayName("bottomLayExposureTime")] public float BottomLayExposureTime { get; set; } = 35; // 35s
            [DisplayName("bottomLayerExposureTime")] public float BottomLayerExposureTime { get; set; } = 35; // 35s
            [DisplayName("normalDropSpeed")] public float RetractSpeed { get; set; } = 150; // 150 mm/m
            [DisplayName("normalLayerLiftSpeed")] public float LiftSpeed { get; set; } = 60; // 60 mm/m
            [DisplayName("normalLayerLiftHeight")] public float LiftHeight { get; set; } = 5; // 5 mm
            [DisplayName("zSlowUpDistance")] public float ZSlowUpDistance { get; set; }
            [DisplayName("bottomLayCount")] public ushort BottomLayCount { get; set; } = 4;
            [DisplayName("bottomLayerCount")] public ushort BottomLayerCount { get; set; } = 4;
            [DisplayName("mirror")] public byte Mirror { get; set; } // 0/1
            [DisplayName("totalLayer")] public uint LayerCount { get; set; }
            [DisplayName("bottomLayerLiftHeight")] public float BottomLiftHeight { get; set; } = 5;
            [DisplayName("bottomLayerLiftSpeed")] public float BottomLiftSpeed { get; set; } = 60;
            [DisplayName("bottomLightOffTime")] public float BottomLightOffTime { get; set; }
            [DisplayName("lightOffTime")] public float LightOffTime { get; set; }
            [DisplayName("bottomPWMLight")] public byte BottomLightPWM { get; set; } = 255;
            [DisplayName("PWMLight")] public byte LayerLightPWM { get; set; } = 255;
            [DisplayName("antiAliasLevel")] public byte AntiAliasing { get; set; } = 1;
        }

        #endregion

        #region Properties
        public Header HeaderSettings { get; } = new Header();

        public override FileFormatType FileType => FileFormatType.Archive;

        public override FileExtension[] FileExtensions { get; } = {
            new FileExtension("zip", "Chitubox Zip Files")
        };

        public override Type[] ConvertToFormats { get; } = {
            typeof(UVJFile)
        };

        public override PrintParameterModifier[] PrintParameterModifiers { get; } = {
            PrintParameterModifier.InitialLayerCount,
            PrintParameterModifier.InitialExposureSeconds,
            PrintParameterModifier.ExposureSeconds,

            PrintParameterModifier.BottomLayerOffTime,
            PrintParameterModifier.LayerOffTime,
            PrintParameterModifier.BottomLiftHeight,
            PrintParameterModifier.BottomLiftSpeed,
            PrintParameterModifier.LiftHeight,
            PrintParameterModifier.LiftSpeed,
            PrintParameterModifier.RetractSpeed,

            PrintParameterModifier.BottomLightPWM,
            PrintParameterModifier.LightPWM,
        };

        public override byte ThumbnailsCount { get; } = 2;

        public override Size[] ThumbnailsOriginalSize { get; } = {new Size(954, 850), new Size(168, 150)};

        public override uint ResolutionX
        {
            get => HeaderSettings.ResolutionX;
            set => HeaderSettings.ResolutionX = value;
        }

        public override uint ResolutionY
        {
            get => HeaderSettings.ResolutionY;
            set => HeaderSettings.ResolutionY = value;
        }

        public override byte AntiAliasing => HeaderSettings.AntiAliasing;

        public override float LayerHeight
        {
            get => HeaderSettings.LayerHeight;
            set => HeaderSettings.LayerHeight = value;
        }

        public override uint LayerCount
        {
            set
            {
                HeaderSettings.LayerCount = LayerCount;
                UpdateGCode();
            }
        }

        public override ushort InitialLayerCount => HeaderSettings.BottomLayerCount;

        public override float InitialExposureTime => HeaderSettings.BottomLayerExposureTime;

        public override float LayerExposureTime => HeaderSettings.LayerExposureTime;

        public override float LiftHeight => HeaderSettings.LiftHeight;

        public override float LiftSpeed => HeaderSettings.LiftSpeed;

        public override float RetractSpeed => HeaderSettings.RetractSpeed;

        public override float PrintTime => HeaderSettings.EstimatedPrintTime;

        public override float UsedMaterial => HeaderSettings.Weight;

        public override float MaterialCost => HeaderSettings.Price;

        public override string MaterialName => HeaderSettings.Resin;

        public override string MachineName => HeaderSettings.MachineType;

        public override object[] Configs => new object[] { HeaderSettings };
        #endregion

        #region Methods

        public override void Clear()
        {
            base.Clear();
            GCode = null;
        }

        public override void Encode(string fileFullPath, OperationProgress progress = null)
        {
            base.Encode(fileFullPath, progress);
            using (ZipArchive outputFile = ZipFile.Open(fileFullPath, ZipArchiveMode.Create))
            {
                if (Thumbnails.Length > 0 && !ReferenceEquals(Thumbnails[0], null))
                {
                    using (Stream stream = outputFile.CreateEntry("preview.png").Open())
                    {
                        var vec = new VectorOfByte();
                        CvInvoke.Imencode(".png", Thumbnails[0], vec);
                        stream.WriteBytes(vec.ToArray());
                        stream.Close();
                    }
                }

                if (Thumbnails.Length > 1 && !ReferenceEquals(Thumbnails[1], null))
                {
                    using (Stream stream = outputFile.CreateEntry("preview_cropping.png").Open())
                    {
                        var vec = new VectorOfByte();
                        CvInvoke.Imencode(".png", Thumbnails[1], vec);
                        stream.WriteBytes(vec.ToArray());
                        stream.Close();
                    }
                }

                UpdateGCode();
                outputFile.PutFileContent("run.gcode", GCode.ToString(), ZipArchiveMode.Create);

                for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                {
                    progress.Token.ThrowIfCancellationRequested();
                    Layer layer = this[layerIndex];
                    outputFile.PutFileContent($"{layerIndex + 1}.png", layer.CompressedBytes, ZipArchiveMode.Create);
                    progress++;
                }
            }

            AfterEncode();
        }

        public override void Decode(string fileFullPath, OperationProgress progress = null)
        {
            base.Decode(fileFullPath, progress);

            FileFullPath = fileFullPath;
            using (var inputFile = ZipFile.Open(FileFullPath, ZipArchiveMode.Read))
            {
                var entry = inputFile.GetEntry("run.gcode");
                if (ReferenceEquals(entry, null))
                {
                    Clear();
                    throw new FileLoadException("run.gcode not found", fileFullPath);
                }

                using (TextReader tr = new StreamReader(entry.Open()))
                {
                    string line;
                    GCode = new StringBuilder();
                    while ((line = tr.ReadLine()) != null)
                    {
                        GCode.AppendLine(line);
                        if (string.IsNullOrEmpty(line)) continue;

                        if (line[0] != ';')
                        {
                            continue;
                        }

                        var splitLine = line.Split(':');
                        if (splitLine.Length < 2) continue;

                        foreach (var propertyInfo in HeaderSettings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var displayNameAttribute = propertyInfo.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();
                            if (ReferenceEquals(displayNameAttribute, null)) continue;
                            if (!splitLine[0].Trim(' ', ';').Equals(displayNameAttribute.DisplayName)) continue;
                            Helpers.SetPropertyValue(propertyInfo, HeaderSettings, splitLine[1].Trim());
                        }
                    }
                    tr.Close();
                }

                if (HeaderSettings.LayerCount == 0)
                {
                    foreach (var zipEntry in inputFile.Entries)
                    {
                        if(!zipEntry.Name.EndsWith(".png")) continue;
                        var filename = Path.GetFileNameWithoutExtension(zipEntry.Name);
                        if (!filename.All(char.IsDigit)) continue;
                        if (!uint.TryParse(filename, out var layerIndex)) continue;
                        HeaderSettings.LayerCount = Math.Max(HeaderSettings.LayerCount, layerIndex);
                    }
                }

                LayerManager = new LayerManager(HeaderSettings.LayerCount, this);

                progress.ItemCount = LayerCount;

                var gcode = GCode.ToString();

                for (uint layerIndex = 0; layerIndex < HeaderSettings.LayerCount; layerIndex++)
                {
                    entry = inputFile.GetEntry($"{layerIndex+1}.png");
                    if (ReferenceEquals(entry, null))
                    {
                        Clear();
                        throw new FileLoadException($"Layer {layerIndex+1} not found", fileFullPath);
                    }

                    var startStr = $";LAYER_START:{layerIndex}";
                    var stripGcode = gcode.Substring(gcode.IndexOf(startStr, StringComparison.InvariantCultureIgnoreCase) + startStr.Length);
                    stripGcode = stripGcode.Substring(0, stripGcode.IndexOf(";LAYER_END")).Trim(' ', '\n', '\r', '\t');
                    //var startCurrPos = stripGcode.Remove(0, ";currPos:".Length);

                    var currPos = Regex.Match(stripGcode, ";currPos:([+-]?([0-9]*[.])?[0-9]+)", RegexOptions.IgnoreCase);
                    var exposureTime = Regex.Match(stripGcode, "G4 P(\\d+)", RegexOptions.IgnoreCase);
                    var pwm = Regex.Match(stripGcode, "M106 S(\\d+)", RegexOptions.IgnoreCase);
                    if (layerIndex < InitialLayerCount)
                    {
                        HeaderSettings.BottomLightPWM = byte.Parse(pwm.Groups[1].Value);
                    }
                    else
                    {
                        HeaderSettings.LayerLightPWM = byte.Parse(pwm.Groups[1].Value);
                    }

                    LayerManager[layerIndex] = new Layer(layerIndex, entry.Open(), entry.Name)
                    {
                        PositionZ = float.Parse(currPos.Groups[1].Value),
                        ExposureTime = float.Parse(exposureTime.NextMatch().Groups[1].Value) / 1000f
                    };
                    progress++;
                }

                if (HeaderSettings.LayerCount > 0 && ResolutionX == 0)
                {
                    using (var mat = this[0].LayerMat)
                    {
                        HeaderSettings.ResolutionX = (uint)mat.Width;
                        HeaderSettings.ResolutionY = (uint)mat.Height;
                    }
                }

                entry = inputFile.GetEntry("preview.png");
                if (!ReferenceEquals(entry, null))
                {
                    CvInvoke.Imdecode(entry.Open().ToArray(), ImreadModes.AnyColor, Thumbnails[0]);
                }

                entry = inputFile.GetEntry("preview_cropping.png");
                if (!ReferenceEquals(entry, null))
                {
                    CvInvoke.Imdecode(entry.Open().ToArray(), ImreadModes.AnyColor, Thumbnails[CreatedThumbnailsCount]);
                }
            }

            LayerManager.GetBoundingRectangle(progress);
        }

        public override object GetValueFromPrintParameterModifier(PrintParameterModifier modifier)
        {
            var baseValue = base.GetValueFromPrintParameterModifier(modifier);
            if (!ReferenceEquals(baseValue, null)) return baseValue;
            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLayerOffTime)) return HeaderSettings.BottomLightOffTime;
            if (ReferenceEquals(modifier, PrintParameterModifier.LayerOffTime)) return HeaderSettings.LightOffTime;
            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight)) return HeaderSettings.BottomLiftHeight;
            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed)) return HeaderSettings.BottomLiftSpeed;
            /*if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight)) return PrintParametersSettings.LiftHeight;
            if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed)) return PrintParametersSettings.LiftingSpeed;
            if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed)) return PrintParametersSettings.RetractSpeed;*/

            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightPWM)) return HeaderSettings.BottomLightPWM;
            if (ReferenceEquals(modifier, PrintParameterModifier.LightPWM)) return HeaderSettings.LayerLightPWM;



            return null;
        }

        public override bool SetValueFromPrintParameterModifier(PrintParameterModifier modifier, string value)
        {
            void UpdateLayers()
            {
                for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                {
                    this[layerIndex].ExposureTime = GetInitialLayerValueOrNormal(layerIndex, InitialExposureTime, LayerExposureTime);
                }
            }

            if (ReferenceEquals(modifier, PrintParameterModifier.InitialLayerCount))
            {
                HeaderSettings.BottomLayerCount =
                    HeaderSettings.BottomLayCount = value.Convert<ushort>();
                UpdateLayers();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.InitialExposureSeconds))
            {
                HeaderSettings.BottomLayerExposureTime = value.Convert<float>();
                UpdateLayers();
                UpdateGCode();
                return true;
            }

            if (ReferenceEquals(modifier, PrintParameterModifier.ExposureSeconds))
            {
                HeaderSettings.LayerExposureTime = value.Convert<float>();
                UpdateLayers();
                UpdateGCode();
                return true;
            }

            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLayerOffTime))
            {
                HeaderSettings.BottomLightOffTime = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.LayerOffTime))
            {
                HeaderSettings.LightOffTime = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight))
            {
                HeaderSettings.BottomLiftHeight = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed))
            {
                HeaderSettings.LiftSpeed = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight))
            {
                HeaderSettings.LiftHeight = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed))
            {
                HeaderSettings.LiftSpeed = value.Convert<float>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed))
            {
                HeaderSettings.RetractSpeed = value.Convert<float>();
                UpdateGCode();
                return true;
            }

            if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightPWM))
            {
                HeaderSettings.BottomLightPWM = value.Convert<byte>();
                UpdateGCode();
                return true;
            }
            if (ReferenceEquals(modifier, PrintParameterModifier.LightPWM))
            {
                HeaderSettings.LayerLightPWM = value.Convert<byte>();
                UpdateGCode();
                return true;
            }

            return false;
        }

        public override void SaveAs(string filePath = null, OperationProgress progress = null)
        {
            if (RequireFullEncode)
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    FileFullPath = filePath;
                }
                Encode(FileFullPath, progress);
                return;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                File.Copy(FileFullPath, filePath, true);
                FileFullPath = filePath;
            }

            using (var outputFile = ZipFile.Open(FileFullPath, ZipArchiveMode.Update))
            {
                foreach (var zipentry in outputFile.Entries)
                {
                    if (zipentry.Name.EndsWith(".gcode"))
                    {
                        zipentry.Delete();
                        break;
                    }
                }

                outputFile.PutFileContent("run.gcode", GCode.ToString(), ZipArchiveMode.Update);
            }

            //Decode(FileFullPath, progress);
        }

        public override bool Convert(Type to, string fileFullPath, OperationProgress progress = null)
        {
            if (to == typeof(UVJFile))
            {
                UVJFile defaultFormat = (UVJFile)FindByType(typeof(UVJFile));
                UVJFile file = new UVJFile
                {
                    LayerManager = LayerManager,
                    JsonSettings = new UVJFile.Settings
                    {
                        Properties = new UVJFile.Properties
                        {
                            Size = new UVJFile.Size
                            {
                                X = (ushort)ResolutionX,
                                Y = (ushort)ResolutionY,
                                Millimeter = new UVJFile.Millimeter
                                {
                                    X = HeaderSettings.MachineX,
                                    Y = HeaderSettings.MachineY,
                                },
                                LayerHeight = LayerHeight,
                                Layers = LayerCount
                            },
                            Bottom = new UVJFile.Bottom
                            {
                                LiftHeight = HeaderSettings.BottomLiftHeight,
                                LiftSpeed = HeaderSettings.BottomLiftSpeed,
                                LightOnTime = InitialExposureTime,
                                LightOffTime = HeaderSettings.BottomLightOffTime,
                                LightPWM = HeaderSettings.BottomLightPWM,
                                RetractSpeed = HeaderSettings.RetractSpeed,
                                Count = InitialLayerCount
                                //RetractHeight = LookupCustomValue<float>(Keyword_LiftHeight, defaultFormat.JsonSettings.Properties.Bottom.RetractHeight),
                            },
                            Exposure = new UVJFile.Exposure
                            {
                                LiftHeight = HeaderSettings.LiftHeight,
                                LiftSpeed = HeaderSettings.LiftSpeed,
                                LightOnTime = LayerExposureTime,
                                LightOffTime = HeaderSettings.LightOffTime,
                                LightPWM = HeaderSettings.LayerLightPWM,
                                RetractSpeed = HeaderSettings.RetractSpeed,
                            },
                            AntiAliasLevel = ValidateAntiAliasingLevel()
                        }
                    }
                };

                file.SetThumbnails(Thumbnails);
                file.Encode(fileFullPath, progress);

                return true;
            }

            return false;
        }

        private void UpdateGCode()
        {
            string arch = Environment.Is64BitOperatingSystem ? "64-bits" : "32-bits";
            GCode = new StringBuilder();
            GCode.AppendLine($"; {About.Website} {About.Software} {Assembly.GetExecutingAssembly().GetName().Version} {arch} {DateTime.Now}"); 

            foreach (var propertyInfo in HeaderSettings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var displayNameAttribute = propertyInfo.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();
                if (ReferenceEquals(displayNameAttribute, null)) continue;
                GCode.AppendLine($";{displayNameAttribute.DisplayName}:{propertyInfo.GetValue(HeaderSettings)}");
            }

            GCode.AppendLine();
            GCode.AppendFormat(GCodeStart, Environment.NewLine);

            float lastZPosition = 0;

            for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                var liftHeight = GetInitialLayerValueOrNormal(layerIndex, HeaderSettings.BottomLiftHeight,
                    HeaderSettings.LiftHeight);

                float liftZHeight = (float) Math.Round(liftHeight + this[layerIndex].PositionZ, 2);

                var liftZSpeed = GetInitialLayerValueOrNormal(layerIndex, HeaderSettings.BottomLiftSpeed,
                    HeaderSettings.LiftSpeed);

                var lightOffDelay = GetInitialLayerValueOrNormal(layerIndex, HeaderSettings.BottomLightOffTime,
                    HeaderSettings.LightOffTime) * 1000;

                var pwmValue = GetInitialLayerValueOrNormal(layerIndex, HeaderSettings.BottomLightPWM, HeaderSettings.LayerLightPWM);
                var exposureTime = this[layerIndex].ExposureTime * 1000;

                GCode.AppendLine($";LAYER_START:{layerIndex}");
                GCode.AppendLine($";currPos:{this[layerIndex].PositionZ}");
                GCode.AppendLine($"M6054 \"{layerIndex+1}.png\";show Image");

                // Absolute gcode
                if (liftHeight > 0 && liftZHeight > this[layerIndex].PositionZ)
                {
                    GCode.AppendLine($"G0 Z{liftZHeight} F{liftZSpeed};Z Lift");
                }

                if (lastZPosition < this[layerIndex].PositionZ)
                {
                    GCode.AppendLine($"G0 Z{this[layerIndex].PositionZ} F{HeaderSettings.RetractSpeed};Layer position");
                }
                
                GCode.AppendLine($"G4 P{lightOffDelay};Before cure delay");
                GCode.AppendLine($"M106 S{pwmValue};light on");
                GCode.AppendLine($"G4 P{exposureTime};Cure time");
                GCode.AppendLine("M106 S0;light off");
                GCode.AppendLine(";LAYER_END");
                GCode.AppendLine();

                lastZPosition = this[layerIndex].PositionZ;
            }

            GCode.AppendFormat(GCodeEnd, Environment.NewLine, HeaderSettings.MachineZ);
        }
        #endregion
    }
}
