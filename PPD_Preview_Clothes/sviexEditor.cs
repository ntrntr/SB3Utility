﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Text;
using SlimDX;

using SB3Utility;

namespace PPD_Preview_Clothes
{
	[Plugin]
	public class sviexEditor : IDisposable
	{
		sviexParser SortedParser;
		public ProgressBar progressBar;

		public sviexEditor(sviexParser parser)
		{
			SortedParser = new sviexParser();
			foreach (sviexParser.SubmeshSection section in parser.sections)
			{
				sviexParser.SubmeshSection sortedSection = new sviexParser.SubmeshSection();
				sortedSection.Name = section.Name;
				sortedSection.submeshIdx = section.submeshIdx;
				sortedSection.indices = new ushort[section.indices.Length];
				sortedSection.normals = new Vector3[section.normals.Length];
				for (ushort i = 0; i < section.indices.Length; i++)
				{
					sortedSection.indices[i] = i;
					int dstIdx = section.indices[i];
					sortedSection.normals[dstIdx] = section.normals[i];
				}
				SortedParser.sections.Add(sortedSection);
			}
		}

		public void Dispose()
		{
			SortedParser = null;
		}

		[Plugin]
		public void CopyNearestNormals(object[] srcMeshes, object[] srcSubmeshes, object[] dstMeshes, object[] dstSubmeshes, sviexParser dstParser, double nearVertexThreshold, bool nearestNormal, bool automatic)
		{
			xxFrame[] srcMeshArr = Utility.Convert<xxFrame>(srcMeshes);
			double[] srcSubmeshDoubleIndices = Utility.Convert<double>(srcSubmeshes);
			HashSet<xxSubmesh> srcSubmeshSet = new HashSet<xxSubmesh>();
			int srcSubmeshIdxIdx = 0;
			int srcTotalSubmeshes = 0;
			foreach (xxFrame meshFrame in srcMeshArr)
			{
				int numSubmeshes = (int)srcSubmeshDoubleIndices[srcSubmeshIdxIdx++];
				srcTotalSubmeshes += numSubmeshes;
				for (int i = 0; i < numSubmeshes; i++)
				{
					int srcSubmeshIdx = (int)srcSubmeshDoubleIndices[srcSubmeshIdxIdx++];
					foreach (sviexParser.SubmeshSection section in SortedParser.sections)
					{
						if (section.Name == meshFrame.Name && section.submeshIdx == srcSubmeshIdx)
						{
							xxSubmesh submesh = meshFrame.Mesh.SubmeshList[srcSubmeshIdx];
							srcSubmeshSet.Add(submesh);
							break;
						}
					}
				}
			}
			if (srcTotalSubmeshes != srcSubmeshSet.Count)
			{
				Report.ReportLog("Not all source submeshes exist in " + SortedParser.Name + ". Using only " + srcSubmeshSet.Count + ".");
			}

			xxFrame[] dstMeshArr = Utility.Convert<xxFrame>(dstMeshes);
			double[] dstSubmeshDoubleIndices = Utility.Convert<double>(dstSubmeshes);
			List<xxSubmesh> dstSubmeshList = new List<xxSubmesh>();
			int dstSubmeshIdxIdx = 0;
			foreach (xxFrame meshFrame in dstMeshArr)
			{
				int numSubmeshes = (int)dstSubmeshDoubleIndices[dstSubmeshIdxIdx++];
				if (numSubmeshes == -1)
				{
					dstSubmeshList.AddRange(meshFrame.Mesh.SubmeshList);
				}
				else
				{
					for (int i = 0; i < numSubmeshes; i++)
					{
						int dstSubmeshIdx = (int)dstSubmeshDoubleIndices[dstSubmeshIdxIdx++];
						xxSubmesh submesh = meshFrame.Mesh.SubmeshList[dstSubmeshIdx];
						dstSubmeshList.Add(submesh);
					}
				}
			}

			sviexParser newParser = new sviexParser();
			foreach (xxFrame dstMeshFrame in dstMeshArr)
			{
				foreach (xxSubmesh dstSubmesh in dstMeshFrame.Mesh.SubmeshList)
				{
					if (!dstSubmeshList.Contains(dstSubmesh))
					{
						continue;
					}

					if (progressBar != null)
					{
						progressBar.Maximum += dstSubmesh.VertexList.Count;
					}
					sviexParser.SubmeshSection newSection = new sviexParser.SubmeshSection();
					newSection.Name = dstMeshFrame.Name;
					newSection.submeshIdx = dstMeshFrame.Mesh.SubmeshList.IndexOf(dstSubmesh);
					newSection.indices = new ushort[dstSubmesh.VertexList.Count];
					newSection.normals = new Vector3[dstSubmesh.VertexList.Count];
					for (ushort i = 0; i < dstSubmesh.VertexList.Count; i++)
					{
						xxVertex dstVertex = dstSubmesh.VertexList[i];
						if (automatic)
						{
							nearestNormal = false;
							for (int j = 0; j < dstSubmesh.VertexList.Count; j++)
							{
								if (j != i)
								{
									xxVertex vert = dstSubmesh.VertexList[j];
									double distSquare = (vert.Position.X - dstVertex.Position.X) * (vert.Position.X - dstVertex.Position.X)
										+ (vert.Position.Y - dstVertex.Position.Y) * (vert.Position.Y - dstVertex.Position.Y)
										+ (vert.Position.Z - dstVertex.Position.Z) * (vert.Position.Z - dstVertex.Position.Z);
									if (distSquare <= nearVertexThreshold)
									{
										nearestNormal = true;
										break;
									}
								}
							}
						}

						Dictionary<xxFrame, Dictionary<xxSubmesh, List<int>>> bestFindings = new Dictionary<xxFrame, Dictionary<xxSubmesh, List<int>>>();
						int totalFindings = 0;
						xxFrame bestMeshFrame = null;
						xxSubmesh bestSubmesh = null;
						int bestIdx = -1;
						double bestDist = double.MaxValue;
						foreach (xxFrame srcMeshFrame in srcMeshArr)
						{
							Dictionary<xxSubmesh, List<int>> bestSubmeshFindings = new Dictionary<xxSubmesh, List<int>>();
							foreach (xxSubmesh srcSubmesh in srcMeshFrame.Mesh.SubmeshList)
							{
								if (!srcSubmeshSet.Contains(srcSubmesh))
								{
									continue;
								}

								List<int> bestIndexFindings = new List<int>(srcSubmesh.VertexList.Count);
								for (int j = 0; j < srcSubmesh.VertexList.Count; j++)
								{
									xxVertex srcVertex = srcSubmesh.VertexList[j];
									double distSquare = (srcVertex.Position.X - dstVertex.Position.X) * (srcVertex.Position.X - dstVertex.Position.X)
										+ (srcVertex.Position.Y - dstVertex.Position.Y) * (srcVertex.Position.Y - dstVertex.Position.Y)
										+ (srcVertex.Position.Z - dstVertex.Position.Z) * (srcVertex.Position.Z - dstVertex.Position.Z);
									if (distSquare <= nearVertexThreshold)
									{
										bestIndexFindings.Add(j);
										totalFindings++;
										continue;
									}
									if (totalFindings == 0 && distSquare < bestDist)
									{
										bestMeshFrame = srcMeshFrame;
										bestSubmesh = srcSubmesh;
										bestIdx = j;
										bestDist = distSquare;
									}
								}
								if (bestIndexFindings.Count > 0)
								{
									bestSubmeshFindings.Add(srcSubmesh, bestIndexFindings);
								}
							}
							if (bestSubmeshFindings.Count > 0)
							{
								bestFindings.Add(srcMeshFrame, bestSubmeshFindings);
							}
						}
						if (totalFindings > 0)
						{
							Vector3 normalSummed = new Vector3();
							Vector3 normalNearest = new Vector3();
							double nearestDist = Double.MaxValue;
							foreach (var finding in bestFindings)
							{
								foreach (sviexParser.SubmeshSection srcSection in SortedParser.sections)
								{
									if (srcSection.Name == finding.Key.Name)
									{
										foreach (var submeshFinding in finding.Value)
										{
											if (srcSection.submeshIdx == finding.Key.Mesh.SubmeshList.IndexOf(submeshFinding.Key))
											{
												foreach (int j in submeshFinding.Value)
												{
													if (nearestNormal)
													{
														double distSquare = (srcSection.normals[j].X - dstVertex.Normal.X) * (srcSection.normals[j].X - dstVertex.Normal.X)
															+ (srcSection.normals[j].Y - dstVertex.Normal.Y) * (srcSection.normals[j].Y - dstVertex.Normal.Y)
															+ (srcSection.normals[j].Z - dstVertex.Normal.Z) * (srcSection.normals[j].Z - dstVertex.Normal.Z);
														if (distSquare < nearestDist)
														{
															normalNearest = srcSection.normals[j];
															nearestDist = distSquare;
														}
													}
													else
													{
														normalSummed += srcSection.normals[j];
													}
												}
											}
										}
									}
								}
							}
							if (totalFindings > 1)
							{
								normalSummed.Normalize();
							}

							newSection.indices[i] = i;
							newSection.normals[i] = nearestNormal ? normalNearest : normalSummed;
						}
						else
						{
							int bestSubmeshIdx = bestMeshFrame.Mesh.SubmeshList.IndexOf(bestSubmesh);
							foreach (sviexParser.SubmeshSection srcSection in SortedParser.sections)
							{
								if (srcSection.Name == bestMeshFrame.Name && srcSection.submeshIdx == bestSubmeshIdx)
								{
									newSection.indices[i] = i;
									newSection.normals[i] = srcSection.normals[bestIdx];
									break;
								}
							}
						}

						if (progressBar != null)
						{
							progressBar.PerformStep();
						}
					}
					newParser.sections.Add(newSection);
				}
			}

			dstParser.sections = newParser.sections;
		}
	}
}