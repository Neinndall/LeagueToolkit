<p align="center">

  <img src="resources/LT_Logo_Transparent.png" width="200"/> 

  <h1 align="center">League Toolkit</h1>

  <p align="center">
    LeagueToolkit is a library for parsing and editing assets from League of Legends
  </p>
</p>

## Discord
If you want to talk to the developers or other people in the community, join our discord server:

<table>
  <tbody>
    <tr>
      <td><img width=64 height=64 src="https://cdn.worldvectorlogo.com/logos/discord.svg"></td>
      <td><h1>https://discord.gg/B36wgabjmD</h1></td>
    </tr>
  </tbody>
</table> 


## Render meshes (.gmesh / .tmesh)

RenderMesh reads the shared version 1 format. The reader retains the magic, bounds,
vertex layouts, raw streams, absolute 16-bit indices and material ranges. It validates
stream lengths, duplicate attributes, index bounds and submesh bounds. Write preserves
the serialized layouts and attribute precision; streams passed to Read/Write remain open.

Example:

    using var input = File.OpenRead("effect.gmesh");
    var mesh = LeagueToolkit.Core.Mesh.RenderMesh.Read(input);
    var position = mesh.ReadVector3(LeagueToolkit.Core.Memory.ElementName.Position, 0);
    var normal = mesh.ReadVector3(LeagueToolkit.Core.Memory.ElementName.Normal, 0);
    var uv = mesh.ReadVector2(LeagueToolkit.Core.Memory.ElementName.Texcoord0, 0);
    using var output = File.Create("effect-copy.gmesh");
    mesh.Write(output);

Attributes can be absent; TryGetAccessor checks their presence before reading.
Float and half-float vector attributes are widened by ReadVector2/ReadVector3.
Packed colors and indices remain accessible through the existing vertex accessors.
No existing ElementFormat enum values are changed.

GMESH has the known GMSH signature. TMESH uses the same layout, but its shipped magic
has not been confirmed by the reference implementation. The reader accepts and retains
any four-byte magic, validates version 1, and file identification recognizes TMESH
by extension rather than assuming a signature.

Format references:
[LeagueToolkit render format](https://github.com/LeagueToolkit/league-toolkit/blob/main/crates/ltk_mesh/src/render/mod.rs),
[reader](https://github.com/LeagueToolkit/league-toolkit/blob/main/crates/ltk_mesh/src/render/read.rs),
[LTK Manager integration](https://github.com/LeagueToolkit/ltk-manager/commit/26993cc2c488b5388a2041a25f8bfe628f37644c).
