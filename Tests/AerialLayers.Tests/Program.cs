using CLV_CivilTools.Gis;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
foreach (string label in new[] { "Nearmap", "NEARMAP", "Nearmap_2026", "2026 Near Map", "Near-map aerial", "(2026 - January) LasVegas", "(2025 - December) Las Vegas_2", "LasVegas", "Library://Aerials/(2026%20-%20January)%20LasVegas.LayerDefinition", "Session:abc//Nearmap.FeatureSource" })
    Check(AerialLayerIdentity.IsAerial(label), "known aerial: " + label);
foreach (string label in new[] { "", "Street_Centerlines", "Assessor Street Centerlines", "Parcels", "NV83.NCRS-LVF", "Sections", "LasVegas_Parcels", "LasVegasRoads", "Library://LasVegas/Streets.FeatureSource", "Nearmapping", "NotNearmap", "Raster image", "GEOMAP" })
    Check(!AerialLayerIdentity.IsAerial(label), "unrelated layer: " + label);
Check(!AerialLayerIdentity.IsAerial(null, " ", "unknown"), "unknown identities do not imply aerial");
Check(AerialLayerIdentity.IsAerial("Raster_004", "(2026 - January) LasVegas", "Session:abc//source.FeatureSource"), "legend resolves generic internal name");
Check(AerialLayerIdentity.IsAerial("Raster_004", "Renamed", "Library://Nearmap/Source.FeatureSource"), "resource resolves renamed layer");
Check(AerialLayerIdentity.IsAerialGroup("Aerial Imagery", "Renamed"), "group internal name retains ownership");
Check(AerialLayerIdentity.IsAerialGroup("Renamed", " AERIAL IMAGERY "), "group legend retains ownership");
Check(!AerialLayerIdentity.IsAerialGroup("Map Base", "Aerial Imagery Reference"), "other groups not substring matched");
Check(!AerialLayerIdentity.IsAerialGroup(null, null), "missing group not owned");
Console.WriteLine($"PASS: {passed} aerial identity checks, generic internal names, legends/resources, group ownership and unrelated GIS exclusions.");
