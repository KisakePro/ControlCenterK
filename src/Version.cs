// Numéro de version unique de l'application ET de l'installateur.
// Pour publier une nouvelle version : modifier Current ici, compléter CHANGELOG.md,
// puis créer le tag Git correspondant (ex. v0.5) : GitHub Actions construit et publie la Release.

[assembly: System.Reflection.AssemblyVersion(ControlCenterK.AppVersion.Assembly)]
[assembly: System.Reflection.AssemblyFileVersion(ControlCenterK.AppVersion.Assembly)]

namespace ControlCenterK
{
    public static class AppVersion
    {
        public const string Current = "0.7.3.2";

        /// <summary>Version d'assembly (4 nombres) : Current complété par .0 si besoin.</summary>
        public const string Assembly = "0.7.3.2";

        /// <summary>Dépôt GitHub où sont publiées les versions (Releases).</summary>
        public const string Repo = "KisakePro/ControlCenterK";

        public const string RepoUrl = "https://github.com/" + Repo;
    }
}
