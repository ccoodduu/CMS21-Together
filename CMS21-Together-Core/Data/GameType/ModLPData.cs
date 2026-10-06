using System;
using System.Runtime.Serialization;

namespace CMS21_Together_Core.Data.GameType;

[Serializable]
public class ModLPData
{
	public string LicensePlateNumberFront;
	public string LicensePlateNumberRear;
	public string FactoryLicensePlateNumber;
	public string LicensePlateFrontTex;
	public string LicensePlateRearTex;
	[OptionalField] public string Name;
	[OptionalField] public string Custom;
}
