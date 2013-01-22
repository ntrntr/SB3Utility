﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using SlimDX;
using SlimDX.Direct3D9;

using SB3Utility;

namespace AiDroidPlugin
{
	[Plugin]
	[PluginOpensFile(".rea")]
	public partial class FormREA : DockContent
	{
		public reaEditor Editor { get; protected set; }
		public string EditorVar { get; protected set; }
		public string ParserVar { get; protected set; }
		public string FormVar { get; protected set; }

		private bool loadedAnimationClip = false;

		private int animationId;
		private KeyframedAnimationSet animationSet = null;

		private Timer renderTimer = new Timer();
		private DateTime startTime;
		private double trackPos = 0;
		private bool play = false;
		private bool trackEnabled = false;
		private bool userTrackBar = true;

		public float AnimationSpeed { get; set; }

		public FormREA(string path, string variable)
		{
			try
			{
				InitializeComponent();

				this.ShowHint = DockState.Document;
				this.Text = Path.GetFileName(path);
				this.ToolTipText = path;

				ParserVar = Gui.Scripting.GetNextVariable("reaParser");
				string parserCommand = ParserVar + " = OpenREA(path=\"" + path + "\")";
				reaParser parser = (reaParser)Gui.Scripting.RunScript(parserCommand);

				EditorVar = Gui.Scripting.GetNextVariable("reaEditor");
				string editorCommand = EditorVar + " = reaEditor(parser=" + ParserVar + ")";
				Editor = (reaEditor)Gui.Scripting.RunScript(editorCommand);

				FormVar = variable;

				Init();
				LoadREA();

				List<DockContent> formREAList;
				if (Gui.Docking.DockContents.TryGetValue(typeof(FormREA), out formREAList))
				{
					var listCopy = new List<FormREA>(formREAList.Count);
					for (int i = 0; i < formREAList.Count; i++)
					{
						listCopy.Add((FormREA)formREAList[i]);
					}

					foreach (var form in listCopy)
					{
						if (form != this)
						{
							if (form.ToolTipText == this.ToolTipText)
							{
								form.Close();
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		public FormREA(fpkParser fpkParser, string reaParserVar)
		{
			try
			{
				InitializeComponent();

				reaParser parser = (reaParser)Gui.Scripting.Variables[reaParserVar];

				this.ShowHint = DockState.Document;
				this.Text = parser.Name;
				this.ToolTipText = fpkParser.FilePath + @"\" + parser.Name;

				ParserVar = reaParserVar;

				EditorVar = Gui.Scripting.GetNextVariable("reaEditor");
				Editor = (reaEditor)Gui.Scripting.RunScript(EditorVar + " = reaEditor(parser=" + ParserVar + ")");

				Init();
				LoadREA();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void CustomDispose()
		{
			try
			{
				UnloadREA();

				if (FormVar != null)
				{
					Gui.Scripting.Variables.Remove(ParserVar);
					Gui.Scripting.Variables.Remove(FormVar);
				}
				Gui.Scripting.Variables.Remove(EditorVar);
				Editor.Dispose();
				Editor = null;
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void Init()
		{
			AnimationSpeed = Decimal.ToSingle(numericAnimationSpeed.Value);

			Gui.Docking.ShowDockContent(this, Gui.Docking.DockEditors, ContentCategory.Animations);
		}

		private void LoadREA()
		{
			if (Editor.Parser.ANIC != null)
			{
				animationSet = CreateAnimationSet();
				if (animationSet != null)
				{
					animationId = Gui.Renderer.AddAnimationSet(animationSet);

					renderTimer.Interval = 10;
					renderTimer.Tick += new EventHandler(renderTimer_Tick);
					Play();
				}

				textBoxANICunk1.Text = Editor.Parser.ANIC.unk1.ToString();
				textBoxANICunk2.Text = Editor.Parser.ANIC.unk2.ToString();
				List<reaAnimationTrack> animationTrackList = Editor.Parser.ANIC.ChildList;
				createAnimationTrackListView(animationTrackList);
				animationSetMaxKeyframes(animationTrackList);

				Gui.Renderer.RenderObjectAdded += new EventHandler(Renderer_RenderObjectAdded);
			}
			else
			{
				animationSetMaxKeyframes(null);
			}
		}

		private void UnloadREA()
		{
			try
			{
				if (Editor.Parser.ANIC != null)
				{
					if (animationSet != null)
					{
						Pause();
						renderTimer.Tick -= renderTimer_Tick;
						Gui.Renderer.RemoveAnimationSet(animationId);
						Gui.Renderer.ResetPose();
						animationSet.Dispose();
						animationSet = null;
					}

					Gui.Renderer.RenderObjectAdded -= new EventHandler(Renderer_RenderObjectAdded);
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void Renderer_RenderObjectAdded(object sender, EventArgs e)
		{
			if (trackEnabled)
			{
				EnableTrack();
			}
			SetTrackPosition(trackPos);
			AdvanceTime(0);
		}

		private void animationSetMaxKeyframes(List<reaAnimationTrack> animationTrackList)
		{
			int max = 0;
			if (animationTrackList != null)
			{
				foreach (reaAnimationTrack animationTrack in animationTrackList)
				{
					int numKeyframes = animationTrack.scalings.Length - 1;
					if (numKeyframes > max)
					{
						max = numKeyframes;
					}
				}
			}

			labelSkeletalRender.Text = "/ " + max;
			numericAnimationKeyframe.Maximum = max;
			trackBarAnimationKeyframe.Maximum = max;
/*			numericAnimationKeyframeStart.Maximum = max;
			numericAnimationKeyframeEnd.Maximum = max;*/
		}

		private void createAnimationTrackListView(List<reaAnimationTrack> animationTrackList)
		{
			if (animationTrackList.Count > 0)
			{
				listViewAnimationTrack.BeginUpdate();
				listViewAnimationTrack.Items.Clear();
				for (int i = 0; i < animationTrackList.Count; i++)
				{
					reaAnimationTrack track = animationTrackList[i];
					ListViewItem item = new ListViewItem(new string[] { track.boneFrame.ToString(), track.scalings.Length.ToString() + "/" + track.rotations.Length.ToString() + "/" + track.translations.Length.ToString(), (track.scalings[track.scalings.Length - 1].index + 1).ToString() + "/" + (track.rotations[track.rotations.Length - 1].index + 1).ToString() + "/" + (track.translations[track.translations.Length - 1].index + 1).ToString() });
					item.Tag = track;
					listViewAnimationTrack.Items.Add(item);
				}
				listViewAnimationTrack.EndUpdate();
			}
		}

		KeyframedAnimationSet CreateAnimationSet()
		{
			var trackList = Editor.Parser.ANIC.ChildList;
			if ((trackList == null) || (trackList.Count <= 0))
			{
				return null;
			}

			KeyframedAnimationSet set = new KeyframedAnimationSet("SetName", 1, PlaybackType.Once, trackList.Count, new CallbackKey[0]);
			for (int i = 0; i < trackList.Count; i++)
			{
				var track = trackList[i];
				ScaleKey[] scaleKeys = new ScaleKey[track.scalings.Length];
				RotationKey[] rotationKeys = new RotationKey[track.rotations.Length];
				TranslationKey[] translationKeys = new TranslationKey[track.translations.Length];
				set.RegisterAnimationKeys(track.boneFrame, scaleKeys, rotationKeys, translationKeys);
				for (int j = 0; j < track.scalings.Length; j++)
				{
					float time = track.scalings[j].index;

					ScaleKey scale = new ScaleKey();
					scale.Time = time;
					scale.Value = track.scalings[j].value;
					//scaleKeys[j] = scale;
					set.SetScaleKey(i, j, scale);
				}
				for (int j = 0; j < track.rotations.Length; j++)
				{
					float time = track.rotations[j].index;

					RotationKey rotation = new RotationKey();
					rotation.Time = time;
					rotation.Value = /*Quaternion.Invert*/(track.rotations[j].value);
					//rotationKeys[j] = rotation;
					set.SetRotationKey(i, j, rotation);
				}
				for (int j = 0; j < track.translations.Length; j++)
				{
					float time = track.translations[j].index;

					TranslationKey translation = new TranslationKey();
					translation.Time = time;
					translation.Value = track.translations[j].value;
					//translationKeys[j] = translation;
					set.SetTranslationKey(i, j, translation);
				}
			}

			return set;
		}

		void SetTrackPosition(double position)
		{
			Gui.Renderer.SetTrackPosition(animationId, position);
			trackPos = position;
		}

		void AdvanceTime(double time)
		{
			Gui.Renderer.AdvanceTime(animationId, time, null);
			trackPos += time;
		}

		public void Play()
		{
			if (loadedAnimationClip)
			{
				SetTrackPosition(0);
				AdvanceTime(0);
			}

			this.play = true;
			this.startTime = DateTime.Now;
			renderTimer.Start();
			buttonAnimationPlayPause.ImageIndex = 1;
		}

		public void Pause()
		{
			this.play = false;
			renderTimer.Stop();
			buttonAnimationPlayPause.ImageIndex = 0;
		}

		public void AnimationSetClip(int idx)
		{
			bool play = this.play;
			Pause();

			if (loadedAnimationClip)
			{
				loadedAnimationClip = false;
			}

			if (idx < 0)
			{
				loadedAnimationClip = false;
				DisableTrack();
			}
			else
			{
				EnableTrack();
				SetTrackPosition(0);
				AdvanceTime(0);

				loadedAnimationClip = true;

				SetKeyframeNum(0);
			}

			if (play)
			{
				Play();
			}
		}

		private void EnableTrack()
		{
			Gui.Renderer.EnableTrack(animationId);
			trackEnabled = true;
		}

		private void DisableTrack()
		{
			Gui.Renderer.DisableTrack(animationId);
			trackEnabled = false;
		}

		private void SetKeyframeNum(int num)
		{
			if ((num >= 0) && (num <= numericAnimationKeyframe.Maximum))
			{
				userTrackBar = false;
				numericAnimationKeyframe.Value = num;
				trackBarAnimationKeyframe.Value = num;
				userTrackBar = true;
			}
		}

		private void renderTimer_Tick(object sender, EventArgs e)
		{
			if (play /*&& loadedAnimationClip*/)
			{
				TimeSpan elapsedTime = DateTime.Now - this.startTime;
				if (elapsedTime.TotalSeconds > 0)
				{
					double advanceTime = elapsedTime.TotalSeconds * AnimationSpeed;
					int clipEnd = Editor.Parser.ANIC[0].scalings[Editor.Parser.ANIC[0].scalings.Length - 1].index;
					if ((trackPos + advanceTime) >= clipEnd)
					{
						SetTrackPosition(0);
						AdvanceTime(0);
					}
					else
					{
						AdvanceTime(advanceTime);
					}

					SetKeyframeNum((int)trackPos);
					this.startTime = DateTime.Now;
				}
			}
		}

		private void numericAnimationSpeed_ValueChanged(object sender, EventArgs e)
		{
			AnimationSpeed = Decimal.ToSingle(numericAnimationSpeed.Value);
		}

		private void buttonAnimationPlayPause_Click(object sender, EventArgs e)
		{
			if (this.play)
			{
				Pause();
			}
			else
			{
				Play();
			}
		}

		private void trackBarAnimationKeyframe_ValueChanged(object sender, EventArgs e)
		{
			if (userTrackBar && (Editor.Parser.ANIC != null))
			{
				Pause();

				if (!trackEnabled)
				{
					EnableTrack();
				}
				SetTrackPosition(Decimal.ToDouble(trackBarAnimationKeyframe.Value));
				AdvanceTime(0);

				userTrackBar = false;
				numericAnimationKeyframe.Value = trackBarAnimationKeyframe.Value;
				userTrackBar = true;
			}
		}

		private void numericAnimationKeyframe_ValueChanged(object sender, EventArgs e)
		{
			if (userTrackBar && (Editor.Parser.ANIC != null))
			{
				Pause();

				if (!trackEnabled)
				{
					EnableTrack();
				}
				SetTrackPosition((double)numericAnimationKeyframe.Value);
				AdvanceTime(0);

				userTrackBar = false;
				trackBarAnimationKeyframe.Value = Decimal.ToInt32(numericAnimationKeyframe.Value);
				userTrackBar = true;
			}
		}

		private void listViewAnimationTrack_AfterLabelEdit(object sender, LabelEditEventArgs e)
		{
			try
			{
				if (e.Label != null)
				{
					string name = e.Label.Trim();
					if (name == String.Empty)
					{
						e.CancelEdit = true;
					}
					else
					{
						reaAnimationTrack keyframeList = (reaAnimationTrack)listViewAnimationTrack.Items[e.Item].Tag;
						Gui.Scripting.RunScript(EditorVar + ".RenameTrack(track=\"" + keyframeList.boneFrame + "\", newName=\"" + e.Label.Trim() + "\")");
						UnloadREA();
						LoadREA();
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonAnimationTrackRemove_Click(object sender, EventArgs e)
		{
			if (listViewAnimationTrack.SelectedItems.Count <= 0)
				return;

			try
			{
				foreach (ListViewItem item in listViewAnimationTrack.SelectedItems)
				{
					Gui.Scripting.RunScript(EditorVar + ".RemoveTrack(track=\"" + ((reaAnimationTrack)item.Tag).boneFrame + "\")");
				}
				UnloadREA();
				LoadREA();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}
	}
}
