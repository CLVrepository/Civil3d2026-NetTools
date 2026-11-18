using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Pipe = Autodesk.Civil.DatabaseServices.Pipe;

namespace CLV_CivilTools.Ufls
{
    public static class PipeStakeLabelCommands
    {
        private const string Key="CLV_PIPE_STAKE_LABEL_V1", Style="CLV-Non Anno";
        private const string Layer="V-SURV-CHCK";
        private sealed record Shot(ObjectId Id,string Name,Point3d Position);
        private sealed record Measurement(PipeStakeMath.LabelValues Values,PipeStakeMath.Projection Projection);
        private sealed class Snapshot
        {
            public int Version { get; set; }=1;
            public string DrawingFingerprint { get; set; }="";
            public string PipeHandle { get; set; }="";
            public string PointHandle { get; set; }="";
            public string AlignmentHandle { get; set; }="";
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double PipeX { get; set; }
            public double PipeY { get; set; }
            public string LabelText { get; set; }="";
            public DateTime MeasuredUtc { get; set; }
        }
        [CommandMethod("UFLS-PIPE-STAKE-LABEL",CommandFlags.Modal)]
        public static void Place() => Run(d =>
        {
            ObjectId pipe=PickPipe(d.Editor); if(pipe.IsNull)return;
            ObjectId alignment=PickAlignment(d.Editor,out bool cancelled); if(cancelled)return;
            double height=0.1;
            bool askHeight=true;
            while(true)
            {
                var options=new PromptEntityOptions("\nSelect check point label/marker <finish>: ") { AllowNone=true };
                options.SetRejectMessage("\nSelect a COGO point label/marker or AutoCAD POINT; Number selects an exact COGO point.");
                options.AddAllowedClass(typeof(CogoPoint),false); options.AddAllowedClass(typeof(DBPoint),false);
                foreach(string keyword in new[]{"Number","Location","Pipe","Alignment","Height"})options.Keywords.Add(keyword);
                PromptEntityResult result=d.Editor.GetEntity(options);
                if(result.Status is PromptStatus.Cancel or PromptStatus.None)return;
                Shot? shot=null;
                if(result.Status==PromptStatus.Keyword)
                {
                    switch(result.StringResult)
                    {
                        case "Pipe": pipe=PickPipe(d.Editor); if(pipe.IsNull)return; continue;
                        case "Alignment": alignment=PickAlignment(d.Editor,out cancelled); if(cancelled)return; continue;
                        case "Height": askHeight=true; d.Editor.WriteMessage("\nText height will be requested after the next check point."); continue;
                        case "Number":
                            PromptIntegerResult number=d.Editor.GetInteger(new PromptIntegerOptions("\nCOGO point number: "){AllowNegative=false,AllowZero=false});
                            if(number.Status!=PromptStatus.OK)return;
                            try
                            {
                                ObjectId id=CivilDocument.GetCivilDocument(d.Database).CogoPoints.GetPointByPointNumber((uint)number.Value);
                                shot=ReadShot(d.Database,id);
                            }
                            catch(ArgumentException){d.Editor.WriteMessage("\nThat point number is not present in this drawing.");continue;}
                            break;
                        case "Location":
                            PromptPointResult location=d.Editor.GetPoint("\nPick check location: "); if(location.Status!=PromptStatus.OK)return;
                            shot=new Shot(ObjectId.Null,"PICK LOCATION",location.Value.TransformBy(d.Editor.CurrentUserCoordinateSystem));
                            break;
                    }
                }
                else if(result.Status==PromptStatus.OK)shot=ReadShot(d.Database,result.ObjectId);
                if(shot==null)continue;
                try
                {
                    using(var preview=d.Database.TransactionManager.StartTransaction())
                    {
                        Measurement m=Measure(preview,pipe,alignment,shot);
                        d.Editor.WriteMessage("\n"+m.Values.Pipe+" | INV "+PipeStakeMath.N(m.Values.Invert)+" | OFFSET "+PipeStakeMath.N(m.Values.Offset,"0.00")+" ft");
                        if(m.Values.Clamped)d.Editor.WriteMessage("\nWARNING: outside the selected pipe segment; label will show nearest-end values and distance to that end.");
                        if(!m.Values.Top.HasValue)d.Editor.WriteMessage("\nWall/slab thickness is unavailable; outside top will remain UNKNOWN.");
                    }
                    if(askHeight) { if(!PickHeight(d.Editor,ref height))return; askHeight=false; }
                    PromptPointResult placement=d.Editor.GetPoint("\nPlace pipe information label: "); if(placement.Status!=PromptStatus.OK)return;
                    Point3d wcs=placement.Value.TransformBy(d.Editor.CurrentUserCoordinateSystem);
                    using Transaction tr=d.Database.TransactionManager.StartTransaction();
                    Shot current=shot.Id.IsNull?shot:ReadShot(tr,shot.Id);
                    Measurement report=Measure(tr,pipe,alignment,current);
                    ObjectId style=TextStyle(d.Database,tr), layer=LabelLayer(d,tr);
                    var text=new MText { Location=new Point3d(wcs.X,wcs.Y,0),Attachment=AttachmentPoint.TopLeft,
                        TextHeight=height,TextStyleId=style,LayerId=layer,Width=0,Contents=PipeStakeMath.Label(report.Values) };
                    text.Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer,256);
                    var space=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(d.Database),OpenMode.ForWrite);
                    space.AppendEntity(text);tr.AddNewlyCreatedDBObject(text,true);
                    Save(text,tr,MakeSnapshot(d.Database,pipe,alignment,current,report,text.Contents));
                    tr.Commit();d.Editor.Regen();
                }
                catch(System.Exception ex){d.Editor.WriteMessage("\nPipe label skipped: "+ex.Message);}
            }
        });
        [CommandMethod("UFLS-PIPE-STAKE-REFRESH",CommandFlags.Modal)]
        public static void Refresh() => Run(d =>
        {
            var options=new PromptEntityOptions("\nSelect pipe stake information label to refresh: ");
            options.SetRejectMessage("\nSelect a label created by PIPE STAKE LABEL.");options.AddAllowedClass(typeof(MText),false);
            PromptEntityResult result=d.Editor.GetEntity(options);if(result.Status!=PromptStatus.OK)return;
            using Transaction tr=d.Database.TransactionManager.StartTransaction();
            var text=(MText)tr.GetObject(result.ObjectId,OpenMode.ForRead);
            if(((LayerTableRecord)tr.GetObject(text.LayerId,OpenMode.ForRead)).IsLocked)throw new InvalidOperationException("Label layer is locked.");
            Snapshot old=Read(text,tr);
            if(old.DrawingFingerprint!=d.Database.FingerprintGuid)throw new InvalidOperationException("Label sources belong to a different drawing; place a new label here.");
            ObjectId pipe=Resolve(d.Database,old.PipeHandle), alignment=old.AlignmentHandle.Length==0?ObjectId.Null:Resolve(d.Database,old.AlignmentHandle);
            Shot shot=old.PointHandle.Length==0?new Shot(ObjectId.Null,"PICK LOCATION",new Point3d(old.X,old.Y,old.Z)):ReadShot(tr,Resolve(d.Database,old.PointHandle));
            Measurement report=Measure(tr,pipe,alignment,shot);
            text.UpgradeOpen();text.Contents=PipeStakeMath.Label(report.Values);
            Save(text,tr,MakeSnapshot(d.Database,pipe,alignment,shot,report,text.Contents));text.RecordGraphicsModified(true);
            tr.Commit();d.Editor.Regen();d.Editor.WriteMessage("\nRefreshed source values; label position, height and style retained.");
        });
        private static void Run(Action<Document> action)
        {
            Document? d=AcadApp.DocumentManager.MdiActiveDocument;if(d==null)return;
            try
            {
                if(!d.Database.TileMode)throw new InvalidOperationException("Use the Model tab.");
                if(CivilDocument.GetCivilDocument(d.Database).Settings.DrawingSettings.UnitZoneSettings.DrawingUnits.ToString()!="Feet")
                    throw new InvalidOperationException("This label requires Civil drawing units set to Feet; no unit/datum conversion is performed.");
                action(d);
            }
            catch(Autodesk.AutoCAD.Runtime.Exception ex)when(ex.ErrorStatus==ErrorStatus.UserBreak){}
            catch(System.Exception ex){d.Editor.WriteMessage("\nPIPE STAKE LABEL: "+ex.Message);}
        }
        private static ObjectId PickPipe(Editor ed)
        {
            var options=new PromptEntityOptions("\nSelect Civil pipe/box for check labels: ");
            options.SetRejectMessage("\nSelect a Civil gravity pipe/box.");options.AddAllowedClass(typeof(Pipe),false);
            PromptEntityResult result=ed.GetEntity(options);return result.Status==PromptStatus.OK?result.ObjectId:ObjectId.Null;
        }
        private static ObjectId PickAlignment(Editor ed,out bool cancelled)
        {
            var options=new PromptEntityOptions("\nSelect stationing alignment <none - distance from pipe start>: "){AllowNone=true};
            options.SetRejectMessage("\nSelect a Civil alignment.");options.AddAllowedClass(typeof(Alignment),false);
            PromptEntityResult result=ed.GetEntity(options);cancelled=result.Status==PromptStatus.Cancel;
            return result.Status==PromptStatus.OK?result.ObjectId:ObjectId.Null;
        }
        private static bool PickHeight(Editor ed,ref double height)
        {
            var options=new PromptDoubleOptions("\nLabel text height in drawing feet: "){DefaultValue=height,UseDefaultValue=true,AllowNegative=false,AllowZero=false};
            PromptDoubleResult result=ed.GetDouble(options);if(result.Status!=PromptStatus.OK)return false;
            if(!double.IsFinite(result.Value)||result.Value<0.001||result.Value>10000)throw new ArgumentException("Text height must be 0.001 to 10000.");
            height=result.Value;return true;
        }
        private static Shot ReadShot(Database db,ObjectId id)
        {using Transaction tr=db.TransactionManager.StartTransaction();return ReadShot(tr,id);}
        private static Shot ReadShot(Transaction tr,ObjectId id)
        {
            var item=tr.GetObject(id,OpenMode.ForRead);
            if(item is CogoPoint cp)return new(id,"POINT "+cp.PointNumber.ToString(CultureInfo.InvariantCulture),cp.Location);
            if(item is DBPoint p)return new(id,"ACAD POINT ["+p.Handle+"]",p.Position);
            throw new InvalidOperationException("Source is not a COGO point or AutoCAD POINT.");
        }
        private static PipeStakeMath.XY XY(Point3d p)=>new(p.X,p.Y);
        private static PipeStakeMath.XY XY(Point2d p)=>new(p.X,p.Y);
        private static Measurement Measure(Transaction tr,ObjectId pipeId,ObjectId alignmentId,Shot shot)
        {
            var pipe=tr.GetObject(pipeId,OpenMode.ForRead) as Pipe??throw new InvalidOperationException("Source pipe is missing/invalid.");
            SweptShapeType shape=PipeStakeMath.Required(()=>pipe.CrossSectionalShape,"pipe cross-section");
            if(shape is not (SweptShapeType.Circular or SweptShapeType.Rectangular))
                throw new InvalidOperationException("Only circular and rectangular pipes are supported.");
            Point3d start=PipeStakeMath.Required(()=>pipe.StartPoint,"pipe start coordinates"),end=PipeStakeMath.Required(()=>pipe.EndPoint,"pipe end coordinates");
            PipeSubEntityType subtype=PipeStakeMath.Required(()=>pipe.SubEntityType,"pipe geometry subtype");
            PipeStakeMath.Projection projection;
            if(subtype==PipeSubEntityType.Straight)projection=PipeStakeMath.Line(XY(start),XY(end),XY(shot.Position));
            else if(subtype==PipeSubEntityType.Curved)
            {
                using PipeCurveGeometry geometry=PipeStakeMath.Required(()=>pipe.CurveGeometry,"pipe plan arc");
                using CircularArc2d arc=geometry.GetArc2d();
                bool first=new Point2d(start.X,start.Y).GetDistanceTo(arc.StartPoint)<=1e-6;
                bool last=new Point2d(start.X,start.Y).GetDistanceTo(arc.EndPoint)<=1e-6;
                if(first==last)throw new InvalidOperationException("Cannot associate native pipe start with the plan arc.");
                projection=PipeStakeMath.Arc(XY(start),XY(end),XY(arc.Center),first?arc.IsClockWise:!arc.IsClockWise,
                    Math.Abs(arc.EndAngle-arc.StartAngle),XY(shot.Position));
            }
            else throw new InvalidOperationException("Unsupported pipe geometry subtype.");
            double height=PipeStakeMath.Required(()=>pipe.InnerHeight,"inside pipe height"),width=PipeStakeMath.Required(()=>pipe.InnerDiameterOrWidth,"inside pipe width/diameter");
            if(!double.IsFinite(width)||width<=0)throw new InvalidOperationException("Native inside pipe width/diameter is unavailable.");
            double? wall=PipeStakeMath.ReadThickness(()=>pipe.WallThickness);
            var elevations=PipeStakeMath.Elevations(start.Z,end.Z,height,wall,projection.Fraction);
            string size=shape==SweptShapeType.Rectangular?
                PipeStakeMath.N(width)+" ft W x "+PipeStakeMath.N(height)+" ft H":PipeStakeMath.N(width*12)+" in DIA";
            string? alignmentName=null,station=null,warning=null;
            if(!alignmentId.IsNull)
            {
                var alignment=tr.GetObject(alignmentId,OpenMode.ForRead) as Alignment??throw new InvalidOperationException("Source alignment is missing/invalid.");
                alignmentName=alignment.Name;
                try
                {
                    double raw=0,offset=0;
                    alignment.StationOffset(projection.Foot.X,projection.Foot.Y,ref raw,ref offset);
                    if(!double.IsFinite(raw)||!double.IsFinite(offset))throw new InvalidOperationException("Non-finite alignment result.");
                    station=alignment.GetStationStringWithEquations(raw);
                }
                catch(Autodesk.Civil.PointNotOnEntityException){warning="PIPE LOCATION OUTSIDE SELECTED ALIGNMENT - STATION UNAVAILABLE";}
                catch(ArgumentException){warning="ALIGNMENT STATION UNAVAILABLE - CHECK ALIGNMENT RANGE";}
            }
            string name=PipeStakeMath.ReadText(()=>pipe.Name)??("PIPE ["+pipe.Handle+"]");
            var values=new PipeStakeMath.LabelValues(shot.Name,name,size,Material(pipe),elevations.Invert,elevations.Top,wall,
                projection.Offset,projection.Along,projection.Clamped,alignmentName,station,warning);
            return new(values,projection);
        }
        private static string Material(Pipe pipe)
        {
            return PipeStakeMath.Material(PipeStakeMath.ReadText(()=>pipe.PartFamilyName),
                PipeStakeMath.ReadText(()=>pipe.Description),PipeStakeMath.ReadText(()=>pipe.PartSizeName));
        }
        private static ObjectId TextStyle(Database db,Transaction tr)
        {
            var styles=(TextStyleTable)tr.GetObject(db.TextStyleTableId,OpenMode.ForRead);
            if(!styles.Has(Style))throw new InvalidOperationException("Load text style CLV-Non Anno from the template first.");
            return styles[Style];
        }
        private static ObjectId LabelLayer(Document d,Transaction tr)
        {
            var layers=(LayerTable)tr.GetObject(d.Database.LayerTableId,OpenMode.ForRead);
            if(layers.Has(Layer)&&((LayerTableRecord)tr.GetObject(layers[Layer],OpenMode.ForRead)).IsLocked)
                throw new InvalidOperationException("V-SURV-CHCK is locked.");
            LayerStandards.TryEnsureManagedLayer(d.Database,tr,d.Editor,Layer);
            if(!layers.Has(Layer))throw new InvalidOperationException("Could not create V-SURV-CHCK.");
            var layer=(LayerTableRecord)tr.GetObject(layers[Layer],OpenMode.ForRead);
            if(layer.IsLocked||layer.IsOff||layer.IsFrozen)throw new InvalidOperationException("Turn on/thaw/unlock V-SURV-CHCK before placing labels.");
            return layer.ObjectId;
        }
        private static Snapshot MakeSnapshot(Database db,ObjectId pipe,ObjectId alignment,Shot shot,Measurement report,string text)=>new()
        {
            DrawingFingerprint=db.FingerprintGuid,
            PipeHandle=pipe.Handle.ToString(),PointHandle=shot.Id.IsNull?"":shot.Id.Handle.ToString(),
            AlignmentHandle=alignment.IsNull?"":alignment.Handle.ToString(),X=shot.Position.X,Y=shot.Position.Y,Z=shot.Position.Z,
            PipeX=report.Projection.Foot.X,PipeY=report.Projection.Foot.Y,LabelText=text,MeasuredUtc=DateTime.UtcNow
        };
        private static ObjectId Resolve(Database db,string value)
        {
            if(!long.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out long handle)||handle<=0)
                throw new InvalidOperationException("Invalid source handle in label snapshot.");
            ObjectId id=db.GetObjectId(false,new Handle(handle),0);
            if(id.IsNull||id.IsErased)throw new InvalidOperationException("A source object has been deleted; place a new label.");
            return id;
        }
        private static void Save(MText label,Transaction tr,Snapshot snapshot)
        {
            if(label.ExtensionDictionary.IsNull)label.CreateExtensionDictionary();
            var dictionary=(DBDictionary)tr.GetObject(label.ExtensionDictionary,OpenMode.ForRead);
            string json=JsonSerializer.Serialize(snapshot);
            Xrecord record;
            if(dictionary.Contains(Key))record=(Xrecord)tr.GetObject(dictionary.GetAt(Key),OpenMode.ForWrite);
            else{dictionary.UpgradeOpen();record=new Xrecord();dictionary.SetAt(Key,record);tr.AddNewlyCreatedDBObject(record,true);}
            using var buffer=new ResultBuffer(Enumerable.Range(0,(json.Length+249)/250)
                .Select(i=>new TypedValue((int)DxfCode.Text,json.Substring(i*250,Math.Min(250,json.Length-i*250)))).ToArray());
            record.Data=buffer;
        }
        private static Snapshot Read(MText label,Transaction tr)
        {
            if(label.ExtensionDictionary.IsNull)throw new InvalidOperationException("This is not a pipe stake label.");
            var dictionary=(DBDictionary)tr.GetObject(label.ExtensionDictionary,OpenMode.ForRead);
            if(!dictionary.Contains(Key))throw new InvalidOperationException("This is not a pipe stake label.");
            var record=(Xrecord)tr.GetObject(dictionary.GetAt(Key),OpenMode.ForRead);
            using ResultBuffer data=record.Data??throw new InvalidOperationException("Missing label snapshot.");
            TypedValue[] values=data.AsArray();
            if(values.Length>300||values.Any(v=>v.TypeCode!=(int)DxfCode.Text||v.Value is not string))throw new InvalidOperationException("Invalid label snapshot.");
            Snapshot snapshot=JsonSerializer.Deserialize<Snapshot>(string.Concat(values.Select(v=>(string)v.Value)),new JsonSerializerOptions{MaxDepth=8})
                ??throw new InvalidOperationException("Empty label snapshot.");
            if(snapshot.Version!=1||!double.IsFinite(snapshot.X)||!double.IsFinite(snapshot.Y)||!double.IsFinite(snapshot.Z)
                ||snapshot.PipeHandle==null||snapshot.PointHandle==null||snapshot.AlignmentHandle==null)
                throw new InvalidOperationException("Unsupported or invalid label snapshot.");
            return snapshot;
        }
    }
}
