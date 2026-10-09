// Numéro de version unique de l'application ET de l'installateur.
// Pour publier une nouvelle version : modifier Current ici, compléter CHANGELOG.md,
// puis créer le tag Git correspondant (ex. v0.5) : GitHub Actions construit et publie la Release.

[assembly: System.Reflection.AssemblyVersion(ControlCenterK.AppVersion.Current + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(ControlCenterK.AppVersion.Current + ".0")]

namespace ControlCenterK
{
    public static class AppVersion
    {
        public const string Current = "0.6";

        /// <summary>Dépôt GitHub où sont publiées les versions (Releases).</summary>
        public const string Repo = "KisakePro/ControlCenterK";

        public const string RepoUrl = "https://github.com/" + Repo;
    }
}
